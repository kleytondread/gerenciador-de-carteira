using Wal.Domain.Enums;

namespace Wal.Services.Irpf;

/// <summary>
/// Apuração mensal do ganho em bolsa, no formato do Demonstrativo de Renda Variável do programa do IRPF
/// (Perguntas e Respostas IRPF 2026, perguntas 705 a 722):
///  - operações comuns 15% e day trade 20% (706); FII/Fiagro 20%, em ficha própria;
///  - ganho com ações no mercado à vista é isento quando as alienações de ações do mês somam até R$ 20 mil (707);
///    a isenção não alcança ETF, BDR, FII nem day trade, e as vendas deles não contam no limite;
///  - prejuízo compensa só ganho da mesma espécie (comum com comum, day trade com day trade, FII com FII), em
///    qualquer mês seguinte, inclusive de outros anos (709 a 711). O prejuízo de ações num mês isento também é
///    levado adiante;
///  - IR retido na fonte: 0,005% sobre as alienações (dispensado até R$ 1,00) e 1% sobre o ganho de day trade;
///    deduz do imposto do mês ou dos meses seguintes do mesmo ano (714, 715);
///  - DARF (código 6015) abaixo de R$ 10,00 não é pago: soma ao do mês seguinte.
/// O app só conhece datas (não notas de corretagem), então o IR retido é uma estimativa a conferir nas notas.
/// </summary>
internal static class ApuracaoRendaVariavel
{
    public record Venda(
        int AtivoId, string Ticker, ClasseFiscal Classe, DateOnly Data, decimal Quantidade, decimal ValorAlienacao,
        decimal ValorVenda, decimal Custo, bool DayTrade)
    {
        public decimal Resultado => ValorVenda - Custo;
    }

    public record Darf(int Ano, int Mes, FichaRendaVariavel Ficha, decimal Valor);

    /// <summary>Resultado de uma coluna da ficha (operações comuns, day trade ou FII) num mês.</summary>
    public record Coluna(
        decimal Resultado, decimal PrejuizoAnterior, decimal BaseCalculo, decimal PrejuizoACompensar, decimal Aliquota, decimal ImpostoDevido);

    /// <param name="ImpostoAPagar">Do mês: imposto devido − IR retido compensado.</param>
    /// <param name="AcumuladoAnterior">Imposto de meses anteriores abaixo de R$ 10 que ficou para este.</param>
    /// <param name="ARecolher">O que o DARF deste mês deveria ter: a pagar + acumulado (zero se ficar abaixo de R$ 10).</param>
    public record Mes(
        int Ano, int MesDoAno, FichaRendaVariavel Ficha,
        Coluna Comum, Coluna? DayTrade,
        decimal AlienacoesAcoes, bool AcoesIsentas, decimal GanhoIsentoAcoes,
        decimal IrrfMes, decimal IrrfDayTradeMes, decimal IrrfCompensado, decimal IrrfACompensar,
        decimal ImpostoAPagar, decimal AcumuladoAnterior, decimal ARecolher, decimal ImpostoPago,
        IReadOnlyList<Venda> Vendas)
    {
        public bool TemMovimento => Vendas.Count > 0 || ImpostoPago > 0 || Comum.PrejuizoAnterior > 0 || (DayTrade?.PrejuizoAnterior ?? 0) > 0;

        /// <summary>DARF faltando: há imposto a recolher e o pago não cobre (tolerância de centavos).</summary>
        public decimal DarfPendente => Math.Max(0, ARecolher - ImpostoPago);
    }

    public record Resultado(IReadOnlyList<Mes> Bolsa, IReadOnlyList<Mes> Fii);

    public static Resultado Apurar(IReadOnlyList<Venda> vendas, IReadOnlyList<Darf> darfs, int ultimoAno)
    {
        var tributaveis = vendas.Where(v => v.Classe is not (ClasseFiscal.Tesouro or ClasseFiscal.EtfRendaFixa)).ToList();
        if (tributaveis.Count == 0 && darfs.Count == 0) return new Resultado([], []);

        var primeiroAno = tributaveis.Select(v => v.Data.Year).Concat(darfs.Select(d => d.Ano)).DefaultIfEmpty(ultimoAno).Min();
        var porMes = tributaveis.ToLookup(v => (v.Data.Year, v.Data.Month));
        var pago = darfs.GroupBy(d => (d.Ano, d.Mes, d.Ficha)).ToDictionary(g => g.Key, g => g.Sum(d => d.Valor));

        var bolsa = new List<Mes>();
        var fii = new List<Mes>();
        decimal prejuizoComum = 0, prejuizoDayTrade = 0, prejuizoFii = 0;
        decimal acumuladoBolsa = 0, acumuladoFii = 0;
        decimal irrfBolsaACompensar = 0, irrfFiiACompensar = 0;

        for (var ano = primeiroAno; ano <= ultimoAno; ano++)
        {
            // O IR retido só compensa dentro do próprio ano (pergunta 715)
            irrfBolsaACompensar = 0;
            irrfFiiACompensar = 0;

            for (var mes = 1; mes <= 12; mes++)
            {
                var doMes = porMes[(ano, mes)].ToList();

                // Operações comuns e day trade (ações, ETF, BDR)
                var daBolsa = doMes.Where(v => v.Classe != ClasseFiscal.Fii).ToList();
                var acoesComuns = daBolsa.Where(v => v.Classe == ClasseFiscal.Acao && !v.DayTrade).ToList();
                var alienacoesAcoes = acoesComuns.Sum(v => v.ValorAlienacao);
                var isento = acoesComuns.Count > 0 && alienacoesAcoes <= TabelaIrpf.LimiteIsencaoAcoes;
                var resultadoAcoes = acoesComuns.Sum(v => v.Resultado);
                var outrosComuns = daBolsa.Where(v => v.Classe != ClasseFiscal.Acao && !v.DayTrade).Sum(v => v.Resultado);
                // Mês isento: o ganho com ações vai para Rendimentos Isentos (código 20); a perda continua compensável
                var resultadoComum = outrosComuns + (isento ? Math.Min(resultadoAcoes, 0) : resultadoAcoes);
                var ganhoIsento = isento ? Math.Max(resultadoAcoes, 0) : 0;
                var resultadoDayTrade = daBolsa.Where(v => v.DayTrade).Sum(v => v.Resultado);

                var comum = Compensar(resultadoComum, ref prejuizoComum, TabelaIrpf.AliquotaComum);
                var dayTrade = Compensar(resultadoDayTrade, ref prejuizoDayTrade, TabelaIrpf.AliquotaDayTrade);
                var irrf = IrrfVendas(daBolsa.Where(v => !v.DayTrade));
                var irrfDayTrade = daBolsa.Where(v => v.DayTrade).GroupBy(v => v.Data)
                    .Sum(g => Math.Max(0, g.Sum(v => v.Resultado)) * TabelaIrpf.IrrfDayTrade);
                bolsa.Add(Consolidar(ano, mes, FichaRendaVariavel.OperacoesComuns, comum, dayTrade, alienacoesAcoes, isento, ganhoIsento,
                    irrf, RegrasIrpf.Centavos(irrfDayTrade), ref irrfBolsaACompensar, ref acumuladoBolsa,
                    pago.GetValueOrDefault((ano, mes, FichaRendaVariavel.OperacoesComuns)), daBolsa));

                // FII/Fiagro
                var deFii = doMes.Where(v => v.Classe == ClasseFiscal.Fii).ToList();
                var colunaFii = Compensar(deFii.Sum(v => v.Resultado), ref prejuizoFii, TabelaIrpf.AliquotaFii);
                fii.Add(Consolidar(ano, mes, FichaRendaVariavel.FundosImobiliarios, colunaFii, null, 0, false, 0,
                    IrrfVendas(deFii), 0, ref irrfFiiACompensar, ref acumuladoFii,
                    pago.GetValueOrDefault((ano, mes, FichaRendaVariavel.FundosImobiliarios)), deFii));
            }
        }

        return new Resultado(bolsa, fii);
    }

    private static Coluna Compensar(decimal resultado, ref decimal prejuizo, decimal aliquota)
    {
        var anterior = prejuizo;
        decimal baseCalculo;
        if (resultado > 0)
        {
            baseCalculo = Math.Max(0, resultado - prejuizo);
            prejuizo = Math.Max(0, prejuizo - resultado);
        }
        else
        {
            baseCalculo = 0;
            prejuizo += -resultado;
        }
        return new Coluna(RegrasIrpf.Centavos(resultado), RegrasIrpf.Centavos(anterior), RegrasIrpf.Centavos(baseCalculo), RegrasIrpf.Centavos(prejuizo),
            aliquota, RegrasIrpf.Centavos(baseCalculo * aliquota));
    }

    // 0,005% sobre as alienações de cada dia (o app não tem as notas; o dia faz as vezes da nota), dispensado até R$ 1,00
    private static decimal IrrfVendas(IEnumerable<Venda> vendas) =>
        vendas.GroupBy(v => v.Data)
            .Select(g => RegrasIrpf.Centavos(g.Sum(v => v.ValorAlienacao) * TabelaIrpf.IrrfVendas))
            .Where(valor => valor > 1m)
            .Sum();

    private static Mes Consolidar(
        int ano, int mes, FichaRendaVariavel ficha, Coluna comum, Coluna? dayTrade, decimal alienacoesAcoes, bool isento,
        decimal ganhoIsento, decimal irrf, decimal irrfDayTrade, ref decimal irrfACompensar, ref decimal acumulado,
        decimal impostoPago, List<Venda> vendas)
    {
        var devido = comum.ImpostoDevido + (dayTrade?.ImpostoDevido ?? 0);
        irrfACompensar += irrf + irrfDayTrade;
        var compensado = Math.Min(devido, irrfACompensar);
        irrfACompensar -= compensado;
        var aPagar = devido - compensado;

        var acumuladoAnterior = acumulado;
        var total = aPagar + acumulado;
        decimal aRecolher;
        if (total < TabelaIrpf.DarfMinimo)
        {
            aRecolher = 0;
            acumulado = total;
        }
        else
        {
            aRecolher = total;
            acumulado = 0;
        }
        // DARF pago além do devido do mês (ex.: acumulado pago antes) abate o que ficou acumulado
        if (impostoPago > aRecolher && acumulado > 0)
            acumulado = Math.Max(0, acumulado - (impostoPago - aRecolher));

        return new Mes(ano, mes, ficha, comum, dayTrade, RegrasIrpf.Centavos(alienacoesAcoes), isento, RegrasIrpf.Centavos(ganhoIsento),
            irrf, irrfDayTrade, RegrasIrpf.Centavos(compensado), RegrasIrpf.Centavos(irrfACompensar), RegrasIrpf.Centavos(aPagar),
            RegrasIrpf.Centavos(acumuladoAnterior), RegrasIrpf.Centavos(aRecolher), impostoPago, vendas);
    }
}

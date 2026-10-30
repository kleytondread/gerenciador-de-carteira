using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wal.Data;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services.Common;
using Wal.Services.Dtos;

namespace Wal.Services.Irpf;

/// <summary>Auxiliar do IRPF: monta, por ano-calendário, o que vai em cada ficha do programa da Receita.</summary>
public interface IIrpfService
{
    /// <summary>Nulo: o último ano fechado (ou o atual, se a carteira começou este ano).</summary>
    Task<RelatorioIrpfResponse?> ObterAsync(int? anoCalendario = null);

    Task<ResultadoOperacao<DarfResponse>> RegistrarDarfAsync(RegistrarDarfRequest request);

    Task<ResultadoOperacao> ExcluirDarfAsync(int id);
}

public class IrpfService(AppDbContext db, CorretoraIrpf corretora, TimeProvider relogio) : IIrpfService
{
    private static readonly CultureInfo PtBR = CultureInfo.GetCultureInfo("pt-BR");

    // Proventos com data ex a partir de outubro do ano anterior costumam ser pagos no ano declarado. Os de
    // pagamento mais demorado (ex.: EGIE3, 9 meses) são casados pela importação do extrato do próprio ano.
    private const int MesesAntesParaConferir = 3;

    public async Task<RelatorioIrpfResponse?> ObterAsync(int? anoCalendario = null)
    {
        var movimentacoes = await db.Movimentacoes.AsNoTracking().OrderBy(m => m.Data).ThenBy(m => m.Id).ToListAsync();
        if (movimentacoes.Count == 0) return null;

        var hoje = Pregao.Dia(relogio.GetUtcNow());
        var anos = Enumerable.Range(movimentacoes[0].Data.Year, hoje.Year - movimentacoes[0].Data.Year + 1).Reverse().ToList();
        var ano = anoCalendario is int pedido && anos.Contains(pedido) ? pedido : anos.FirstOrDefault(a => a < hoje.Year, anos[0]);
        var fimAno = new DateOnly(ano, 12, 31);
        var fimAnterior = new DateOnly(ano - 1, 12, 31);

        var ativos = await db.Ativos.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var darfs = await db.DarfsPagos.AsNoTracking().OrderBy(d => d.Ano).ThenBy(d => d.Mes).ToListAsync();
        var motor = await MotorCarteira.CalcularAsync(db);
        var porAtivo = movimentacoes.ToLookup(m => m.AtivoId);

        var pendencias = new List<PendenciaIrpf>();
        var proventos = await ProventosFiscaisAsync(motor, ativos);

        var bens = BensDireitos(ano, porAtivo, ativos, motor.Bonificacoes, fimAnterior, fimAno);
        bens.AddRange(JcpAReceber(proventos, ativos, fimAnterior, fimAno));

        var vendas = ativos.Values
            .SelectMany(a => CustoMedio.Calcular(porAtivo[a.Id], fiscal: true).Vendas.Select(v => new ApuracaoRendaVariavel.Venda(
                a.Id, a.Ticker, Classe(a), v.Data, v.Quantidade, v.ValorAlienacao, v.ValorVenda, v.Custo, v.DayTrade)))
            .ToList();
        var apuracao = ApuracaoRendaVariavel.Apurar(
            vendas, darfs.Select(d => new ApuracaoRendaVariavel.Darf(d.Ano, d.Mes, d.Ficha, d.ValorPrincipal)).ToList(), ano);
        var meses = apuracao.Bolsa.Where(m => m.Ano == ano).ToList();
        var mesesFii = apuracao.Fii.Where(m => m.Ano == ano).ToList();

        var isentos = Isentos(ano, proventos, ativos, porAtivo, motor.Bonificacoes, meses);
        var exclusivos = Exclusivos(ano, proventos, ativos);

        Pendencias(pendencias, ano, hoje, proventos, ativos, bens, isentos, exclusivos, meses, mesesFii, vendas);

        return new RelatorioIrpfResponse(
            ano, anos, ano >= hoje.Year, TabelaIrpf.Conferido(ano), corretora with { Cnpj = Cnpj.Formatar(Cnpj.Normalizar(corretora.Cnpj)) },
            pendencias.OrderBy(p => p.Gravidade).ThenBy(p => p.Aba).ToList(),
            bens.OrderBy(b => b.Codigo.Grupo).ThenBy(b => b.Codigo.Codigo).ThenBy(b => b.Ticker).ToList(),
            isentos, exclusivos,
            meses.Select(ParaResponse).ToList(), mesesFii.Select(ParaResponse).ToList(),
            darfs.Where(d => d.Ano == ano).Select(ParaResponse).ToList());
    }

    public async Task<ResultadoOperacao<DarfResponse>> RegistrarDarfAsync(RegistrarDarfRequest request)
    {
        if (request.Mes is < 1 or > 12 || request.Ano is < 2000 or > 2100)
            return ResultadoOperacao<DarfResponse>.Falha("Informe o mês de apuração.", TipoErroOperacao.Validacao);
        if (request.ValorPrincipal <= 0)
            return ResultadoOperacao<DarfResponse>.Falha("O valor principal deve ser maior que zero.", TipoErroOperacao.Validacao);
        if (request.DataPagamento < new DateOnly(request.Ano, request.Mes, 1))
            return ResultadoOperacao<DarfResponse>.Falha("O pagamento não pode ser antes do mês de apuração.", TipoErroOperacao.Validacao);

        var darf = new DarfPago
        {
            Ano = request.Ano,
            Mes = request.Mes,
            Ficha = request.Ficha,
            ValorPrincipal = RegrasIrpf.Centavos(request.ValorPrincipal),
            DataPagamento = request.DataPagamento
        };
        db.DarfsPagos.Add(darf);
        await db.SaveChangesAsync();
        return ResultadoOperacao<DarfResponse>.Ok(ParaResponse(darf));
    }

    public async Task<ResultadoOperacao> ExcluirDarfAsync(int id)
    {
        var darf = await db.DarfsPagos.FindAsync(id);
        if (darf is null) return ResultadoOperacao.Falha("DARF não encontrado.", TipoErroOperacao.NaoEncontrado);
        db.DarfsPagos.Remove(darf);
        await db.SaveChangesAsync();
        return ResultadoOperacao.Ok();
    }

    // ── Proventos ──

    /// <summary>Uma parcela paga; nas que não vieram da B3, o líquido do JCP é calculado pela alíquota.</summary>
    private record Parcela(DateOnly Data, decimal Bruto, decimal Liquido, bool Calculada);

    /// <summary>Provento como a Receita o enxerga: tipo, crédito (data com), total e parcelas pagas.</summary>
    private record ProventoFiscal(
        int Id, int AtivoId, TipoProvento Tipo, OrigemDado Origem, DateOnly DataEx, DateOnly? DataCom, decimal Quantidade,
        decimal Bruto, IReadOnlyList<Parcela> Parcelas)
    {
        public decimal Aliquota => Tipo == TipoProvento.JCP && DataCom is DateOnly d ? RegrasIrpf.AliquotaJcp(d) : 0;
        public decimal Liquido => RegrasIrpf.Centavos(Bruto * (1 - Aliquota));

        /// <summary>Todo pago pela B3: o líquido é a soma das parcelas, sem cálculo.</summary>
        public bool QuitadoPelaB3 =>
            Origem == OrigemDado.ExtratoB3 && Parcelas.Sum(p => p.Bruto) >= Bruto - Importacao(Bruto, Quantidade);

        public decimal LiquidoDeclarado => QuitadoPelaB3 ? Parcelas.Sum(p => p.Liquido) : Liquido;

        private static decimal Importacao(decimal bruto, decimal quantidade) =>
            ImportacaoB3.ConciliacaoProventosB3.Tolerancia(bruto, quantidade);
    }

    private async Task<List<ProventoFiscal>> ProventosFiscaisAsync(MotorCarteira motor, Dictionary<int, Ativo> ativos)
    {
        var ids = motor.ProventosRecebidos.Select(r => r.ProventoId).ToHashSet();
        var registros = await db.Proventos.AsNoTracking().Include(p => p.Pagamentos)
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var ativoIds = motor.ProventosRecebidos.Where(r => r.Origem == OrigemDado.YahooFinance).Select(r => r.AtivoId).ToHashSet();
        var pregoes = (await db.CotacoesHistoricas.AsNoTracking().Where(c => ativoIds.Contains(c.AtivoId))
                .Select(c => new { c.AtivoId, c.Data }).ToListAsync())
            .GroupBy(c => c.AtivoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DateOnly>)g.Select(c => c.Data).Order().ToList());

        return motor.ProventosRecebidos.Select(r =>
        {
            var registro = registros[r.ProventoId];
            DateOnly? dataCom = r.Origem switch
            {
                OrigemDado.YahooFinance => RegrasIrpf.DataComPelaDataEx(r.DataEx, pregoes.GetValueOrDefault(r.AtivoId) ?? []),
                _ => registro.DataCom
            };
            List<Parcela> parcelas;
            if (r.Origem == OrigemDado.ExtratoB3)
                parcelas = registro.Pagamentos.OrderBy(p => p.Data).Select(p => new Parcela(p.Data, p.ValorBruto, p.ValorLiquido, false)).ToList();
            else
            {
                var aliquota = r.Tipo == TipoProvento.JCP && dataCom is DateOnly d ? RegrasIrpf.AliquotaJcp(d) : 0;
                parcelas = [new Parcela(r.DataPagamento, r.Valor, RegrasIrpf.Centavos(r.Valor * (1 - aliquota)), true)];
            }
            return new ProventoFiscal(r.ProventoId, r.AtivoId, r.Tipo, r.Origem, r.DataEx, dataCom, r.Quantidade, r.Valor, parcelas);
        }).ToList();
    }

    private static List<RendimentoIrpf> Isentos(
        int ano, List<ProventoFiscal> proventos, Dictionary<int, Ativo> ativos, ILookup<int, Movimentacao> porAtivo,
        IReadOnlySet<int> bonificacoes, List<ApuracaoRendaVariavel.Mes> meses)
    {
        var lista = new List<RendimentoIrpf>();

        // 09 — dividendos de ações, pelo ano de cada parcela paga
        lista.AddRange(PorFonte(
            proventos.Where(p => p.Tipo == TipoProvento.Dividendo && Classe(ativos[p.AtivoId]) == ClasseFiscal.Acao),
            p => p.Parcelas.Where(x => x.Data.Year == ano)
                .Select(x => new ComposicaoRendimento(x.Data, $"Dividendo (data ex {p.DataEx:dd/MM/yyyy})", x.Bruto, x.Liquido, x.Calculada)),
            TabelaIrpf.Dividendos, ativos));

        // 18 — bonificações em ações: quantidade × custo atribuído (Perguntas e Respostas, pergunta 721)
        lista.AddRange(PorFonte(
            ativos.Keys.Select(id => (Id: id, Bonificadas: porAtivo[id].Where(m => bonificacoes.Contains(m.Id) && m.Data.Year == ano).ToList()))
                .Where(x => x.Bonificadas.Count > 0)
                .Select(x => x.Id),
            id => porAtivo[id].Where(m => bonificacoes.Contains(m.Id) && m.Data.Year == ano)
                .Select(m => new ComposicaoRendimento(m.Data,
                    $"{m.Quantidade.ToString("0.####", PtBR)} ações × custo atribuído de {m.PrecoUnitario.ToString("C4", PtBR)}",
                    m.Quantidade * m.PrecoUnitario, m.Quantidade * m.PrecoUnitario, false)),
            TabelaIrpf.Bonificacoes, ativos, ativoIdDe: id => id,
            observacao: "Na declaração de 2025 as bonificações só apareceram na discriminação de Bens e Direitos. Confira no programa se este código é o esperado."));

        // 20 — ganho com ações nos meses com alienações de até R$ 20 mil
        var meseIsentos = meses.Where(m => m.GanhoIsentoAcoes > 0).ToList();
        if (meseIsentos.Count > 0)
            lista.Add(new RendimentoIrpf(TabelaIrpf.GanhoAcoesIsento, null, "Ações", "Ganhos líquidos em vendas de ações até R$ 20 mil no mês", null,
                meseIsentos.Sum(m => m.GanhoIsentoAcoes), false,
                meseIsentos.Select(m => new ComposicaoRendimento(new DateOnly(ano, m.MesDoAno, 1),
                    $"{NomeMes(m.MesDoAno)}: vendas de ações de {m.AlienacoesAcoes.ToString("C2", PtBR)}", m.GanhoIsentoAcoes, m.GanhoIsentoAcoes, false)).ToList(),
                "Não vai CNPJ: é um valor único na ficha. ETF e BDR nunca são isentos (ficam na Renda Variável)."));

        // 26 — rendimentos de FII/Fiagro
        lista.AddRange(PorFonte(
            proventos.Where(p => Classe(ativos[p.AtivoId]) == ClasseFiscal.Fii),
            p => p.Parcelas.Where(x => x.Data.Year == ano)
                .Select(x => new ComposicaoRendimento(x.Data, "Rendimento", x.Bruto, x.Liquido, x.Calculada)),
            TabelaIrpf.RendimentoFii, ativos,
            observacao: "Isento quando o fundo tem mais de 100 cotistas e cotas negociadas em bolsa, e você tem menos de 10% das cotas. Confira o código no programa."));

        return lista;
    }

    private static List<RendimentoIrpf> Exclusivos(int ano, List<ProventoFiscal> proventos, Dictionary<int, Ativo> ativos)
    {
        var lista = new List<RendimentoIrpf>();

        // 10 — JCP pelo ano do crédito (data com), pelo líquido
        lista.AddRange(PorFonte(
            proventos.Where(p => p.Tipo == TipoProvento.JCP && p.DataCom?.Year == ano && Classe(ativos[p.AtivoId]) == ClasseFiscal.Acao),
            p => [new ComposicaoRendimento(p.DataCom!.Value,
                $"JCP creditado em {p.DataCom:dd/MM/yyyy} (IR de {p.Aliquota.ToString("P1", PtBR)}); " +
                (p.QuitadoPelaB3 ? $"pago em {string.Join(", ", p.Parcelas.Select(x => x.Data.ToString("dd/MM/yyyy")))}"
                    : p.Origem == OrigemDado.ExtratoB3 ? $"pago {p.Parcelas.Sum(x => x.Liquido).ToString("C2", PtBR)} até agora" : "valor calculado"),
                p.Bruto, p.LiquidoDeclarado, !p.QuitadoPelaB3)],
            TabelaIrpf.Jcp, ativos,
            observacao: "Declara-se pelo ano do crédito, não do pagamento. O que não foi pago até 31/12 também vai em Bens e Direitos (99-07)."));

        // 06 — atualização monetária (Selic) de proventos pagos com atraso, como na declaração de 2025
        lista.AddRange(PorFonte(
            proventos.Where(p => p.Tipo == TipoProvento.Rendimento && Classe(ativos[p.AtivoId]) == ClasseFiscal.Acao),
            p => p.Parcelas.Where(x => x.Data.Year == ano)
                .Select(x => new ComposicaoRendimento(x.Data, "Atualização monetária de provento", x.Bruto, x.Liquido, x.Calculada)),
            TabelaIrpf.AplicacoesFinanceiras, ativos,
            observacao: "Atualização pela Selic de dividendo/JCP pago com atraso. Confira o valor e a ficha no informe de rendimentos da empresa."));

        return lista;
    }

    /// <summary>Uma linha por fonte pagadora (CNPJ; sem CNPJ, por ativo), com o detalhe do que a compõe.</summary>
    private static IEnumerable<RendimentoIrpf> PorFonte<T>(
        IEnumerable<T> origens, Func<T, IEnumerable<ComposicaoRendimento>> composicao, CodigoIrpf codigo,
        Dictionary<int, Ativo> ativos, Func<T, int>? ativoIdDe = null, string? observacao = null)
    {
        ativoIdDe ??= o => o is ProventoFiscal p ? p.AtivoId : throw new InvalidOperationException();
        return origens
            .Select(o => (Ativo: ativos[ativoIdDe(o)], Itens: composicao(o).ToList()))
            .Where(x => x.Itens.Count > 0)
            .GroupBy(x => Cnpj.Normalizar(x.Ativo.Cnpj) ?? "#" + x.Ativo.Id)
            .Select(g =>
            {
                var primeiro = g.OrderBy(x => x.Ativo.Ticker).First().Ativo;
                var itens = g.SelectMany(x => x.Itens).OrderBy(i => i.Data).ToList();
                return new RendimentoIrpf(
                    codigo, primeiro.Id, string.Join(", ", g.Select(x => x.Ativo.Ticker).Distinct().Order()),
                    primeiro.Nome, FormatarCnpj(primeiro.Cnpj), RegrasIrpf.Centavos(itens.Sum(i => i.ValorLiquido)),
                    itens.Any(i => i.Calculado), itens, observacao);
            })
            .OrderBy(r => r.Ticker);
    }

    // ── Bens e Direitos ──

    private List<BemDireitoIrpf> BensDireitos(
        int ano, ILookup<int, Movimentacao> porAtivo, Dictionary<int, Ativo> ativos, IReadOnlySet<int> bonificacoes,
        DateOnly fimAnterior, DateOnly fimAno)
    {
        var lista = new List<BemDireitoIrpf>();
        foreach (var ativo in ativos.Values)
        {
            var movs = porAtivo[ativo.Id].ToList();
            if (movs.Count == 0) continue;
            var anterior = CustoMedio.Calcular(movs.Where(m => m.Data <= fimAnterior), fiscal: true);
            var atual = CustoMedio.Calcular(movs.Where(m => m.Data <= fimAno), fiscal: true);
            if (Zero(anterior.Quantidade) && Zero(atual.Quantidade)) continue;

            var classe = Classe(ativo);
            var codigo = TabelaIrpf.BemDireito(classe);
            var situacaoAnterior = Zero(anterior.Quantidade) ? 0 : RegrasIrpf.Centavos(anterior.CustoTotal);
            var situacaoAtual = Zero(atual.Quantidade) ? 0 : RegrasIrpf.Centavos(atual.CustoTotal);
            var bonificadas = movs.Where(m => bonificacoes.Contains(m.Id) && m.Data.Year == ano).ToList();

            lista.Add(new BemDireitoIrpf(
                codigo, TabelaIrpf.BemDireitoConferido(classe), ativo.Id, ativo.Ticker, ativo.Nome,
                classe == ClasseFiscal.Tesouro ? FormatarCnpj(corretora.Cnpj) : FormatarCnpj(ativo.Cnpj),
                classe != ClasseFiscal.Tesouro,
                Discriminacao(ativo, classe, atual, anterior.Quantidade, ano, bonificadas),
                Zero(anterior.Quantidade) ? 0 : anterior.Quantidade, situacaoAnterior,
                Zero(atual.Quantidade) ? 0 : atual.Quantidade, situacaoAtual,
                classe is ClasseFiscal.Acao or ClasseFiscal.Bdr && situacaoAnterior < 1000 && situacaoAtual < 1000));
        }
        return lista;
    }

    private string Discriminacao(Ativo ativo, ClasseFiscal classe, CustoMedio.Resultado atual, decimal quantidadeAnterior, int ano, List<Movimentacao> bonificadas)
    {
        var nome = Maiusculas(ativo.Nome);
        var (singular, plural, descricao) = classe switch
        {
            ClasseFiscal.Acao => ("ACAO", "ACOES", $"DA EMPRESA {nome}"),
            ClasseFiscal.Bdr => ("BDR", "BDRS", $"DE {nome}"),
            ClasseFiscal.Fii => ("COTA", "COTAS", $"DO FUNDO IMOBILIARIO {nome}"),
            ClasseFiscal.Tesouro => ("TITULO", "TITULOS", $"{nome} (TESOURO DIRETO), CUSTODIADOS NA {Maiusculas(corretora.Nome)}"),
            _ => ("COTA", "COTAS", $"DO FUNDO DE INDICE {nome}")
        };
        string Unidade(decimal q) => q == 1 ? singular : plural;
        var negociacao = classe == ClasseFiscal.Tesouro ? string.Empty : $" CODIGO DE NEGOCIACAO: {ativo.Ticker}.";
        var texto = Zero(atual.Quantidade)
            ? $"{plural} {descricao}.{negociacao} {Quantidade(quantidadeAnterior)} {Unidade(quantidadeAnterior)} {(quantidadeAnterior == 1 ? "VENDIDA" : "VENDIDAS")} EM {ano}; POSICAO ENCERRADA."
            : $"{Quantidade(atual.Quantidade)} {Unidade(atual.Quantidade)} {descricao}.{negociacao} AO PRECO MEDIO DE R$ {Preco(atual.PrecoMedio)}.";
        foreach (var b in bonificadas)
            texto += $" BONIFICACAO EM {b.Data:dd/MM/yyyy}: {Quantidade(b.Quantidade)} {Unidade(b.Quantidade)} COM CUSTO ATRIBUIDO DE R$ {b.PrecoUnitario.ToString("0.00####", PtBR)}" +
                     $" (TOTAL R$ {(b.Quantidade * b.PrecoUnitario).ToString("N2", PtBR)}).";
        return texto;
    }

    /// <summary>99-07: JCP creditado (data com até 31/12) e ainda não pago nessa data, pelo líquido.</summary>
    private static IEnumerable<BemDireitoIrpf> JcpAReceber(
        List<ProventoFiscal> proventos, Dictionary<int, Ativo> ativos, DateOnly fimAnterior, DateOnly fimAno)
    {
        static decimal AReceber(ProventoFiscal p, DateOnly data) =>
            p.DataCom is DateOnly com && com <= data
                ? Math.Max(0, p.LiquidoDeclarado - p.Parcelas.Where(x => x.Data <= data).Sum(x => x.Liquido))
                : 0;

        return proventos
            .Where(p => p.Tipo == TipoProvento.JCP && Classe(ativos[p.AtivoId]) == ClasseFiscal.Acao)
            .GroupBy(p => Cnpj.Normalizar(ativos[p.AtivoId].Cnpj) ?? "#" + p.AtivoId)
            .Select(g =>
            {
                var ativo = ativos[g.First().AtivoId];
                var anterior = RegrasIrpf.Centavos(g.Sum(p => AReceber(p, fimAnterior)));
                var atual = RegrasIrpf.Centavos(g.Sum(p => AReceber(p, fimAno)));
                return new BemDireitoIrpf(
                    TabelaIrpf.JcpAReceber, true, ativo.Id, ativo.Ticker, ativo.Nome, FormatarCnpj(ativo.Cnpj), false,
                    $"JUROS SOBRE CAPITAL PROPRIO CREDITADOS E NAO PAGOS ATE 31/12/{fimAno.Year} - {Maiusculas(ativo.Nome)} ({ativo.Ticker}).",
                    0, anterior, 0, atual, false);
            })
            .Where(b => b.SituacaoAnterior >= 0.01m || b.SituacaoAtual >= 0.01m);
    }

    // ── Pendências ──

    private void Pendencias(
        List<PendenciaIrpf> lista, int ano, DateOnly hoje, List<ProventoFiscal> proventos, Dictionary<int, Ativo> ativos,
        List<BemDireitoIrpf> bens, List<RendimentoIrpf> isentos, List<RendimentoIrpf> exclusivos,
        List<ApuracaoRendaVariavel.Mes> meses, List<ApuracaoRendaVariavel.Mes> mesesFii, List<ApuracaoRendaVariavel.Venda> vendas)
    {
        var inicioConferencia = new DateOnly(ano, 1, 1).AddMonths(-MesesAntesParaConferir);
        foreach (var p in proventos.Where(p => p.Origem == OrigemDado.YahooFinance && p.DataEx >= inicioConferencia && p.DataEx.Year <= ano)
                     .OrderBy(p => p.DataEx))
        {
            var ativo = ativos[p.AtivoId];
            lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.ProventoNaoConferido, GravidadePendencia.Bloqueia,
                Classe(ativo) == ClasseFiscal.Acao ? AbaIrpf.RendimentosIsentos : AbaIrpf.TributacaoExclusiva,
                $"{ativo.Ticker}: provento de data ex {p.DataEx:dd/MM/yyyy} não conferido",
                $"Veio do Yahoo ({p.Bruto.ToString("C2", PtBR)} bruto), que não informa tipo nem data de pagamento. " +
                "Importe o extrato de Movimentação da B3 do período ou corrija o provento à mão.",
                p.AtivoId, p.Id));
        }

        foreach (var p in proventos.Where(p => p.Origem == OrigemDado.ExtratoB3 && p.DataCom is null && p.Tipo == TipoProvento.JCP
                                               && p.Parcelas.Any(x => x.Data.Year is var a && (a == ano || a == ano + 1))))
            lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.ProventoSemDataCom, GravidadePendencia.Bloqueia, AbaIrpf.TributacaoExclusiva,
                $"{ativos[p.AtivoId].Ticker}: JCP pago em {p.Parcelas[0].Data:dd/MM/yyyy} sem data com",
                "Sem a data com não dá para saber o ano do crédito: o JCP fica fora do código 10 até ela ser informada.",
                p.AtivoId, p.Id));

        // CNPJ de quem aparece em alguma ficha (Tesouro usa o da corretora; BDR tem emissor estrangeiro, sem CNPJ)
        var usados = bens.Where(b => b.AtivoId is int && b.Codigo != TabelaIrpf.BemDireito(ClasseFiscal.Tesouro)
                                     && b.Codigo != TabelaIrpf.BemDireito(ClasseFiscal.Bdr)).Select(b => b.AtivoId!.Value)
            .Concat(isentos.Concat(exclusivos).Where(r => r.Codigo != TabelaIrpf.GanhoAcoesIsento && r.AtivoId is int).Select(r => r.AtivoId!.Value))
            .Distinct();
        foreach (var ativo in usados.Select(id => ativos[id]).OrderBy(a => a.Ticker))
        {
            var cnpj = Cnpj.Normalizar(ativo.Cnpj);
            if (cnpj is null)
                lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.CnpjAusente, GravidadePendencia.Bloqueia, AbaIrpf.BensDireitos,
                    $"{ativo.Ticker} sem CNPJ", "Informe o CNPJ da empresa ou do fundo no cadastro do ativo.", ativo.Id));
            else if (!Cnpj.Valido(cnpj))
                lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.CnpjInvalido, GravidadePendencia.Bloqueia, AbaIrpf.BensDireitos,
                    $"{ativo.Ticker}: CNPJ inválido", $"{Cnpj.Formatar(cnpj)} não confere com os dígitos verificadores.", ativo.Id));
            else if (!Cnpj.EhMatriz(cnpj))
                lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.CnpjFilial, GravidadePendencia.Aviso, AbaIrpf.BensDireitos,
                    $"{ativo.Ticker}: CNPJ de filial", $"{Cnpj.Formatar(cnpj)} não é da matriz (/0001). Empresas e fundos são declarados pela matriz.", ativo.Id));
        }

        // Ticker com final de BDR (32 a 35, 39) cadastrado como ação: código e isenção errados
        foreach (var ativo in bens.Where(b => b.AtivoId is int && b.Codigo == TabelaIrpf.BemDireito(ClasseFiscal.Acao)).Select(b => ativos[b.AtivoId!.Value])
                     .Where(a => a.Ticker.Length == 6 && a.Ticker[^2..] is "32" or "33" or "34" or "35" or "39"))
            lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.Alerta, GravidadePendencia.Bloqueia, AbaIrpf.BensDireitos,
                $"{ativo.Ticker} parece um BDR cadastrado como ação",
                "BDR vai no código 04-04, não tem CNPJ e não tem a isenção de R$ 20 mil nas vendas. Se for BDR, troque o tipo no cadastro do ativo.",
                ativo.Id));

        foreach (var m in meses.Concat(mesesFii).Where(m => m.DarfPendente >= 0.01m))
            lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.DarfPendente, GravidadePendencia.Bloqueia, AbaIrpf.RendaVariavel,
                $"DARF de {NomeMes(m.MesDoAno).ToLowerInvariant()}/{ano}{(m.Ficha == FichaRendaVariavel.FundosImobiliarios ? " (FII)" : "")}: {m.DarfPendente.ToString("C2", PtBR)} sem pagamento registrado",
                $"Imposto a recolher {m.ARecolher.ToString("C2", PtBR)}, pago {m.ImpostoPago.ToString("C2", PtBR)}. Se ainda não pagou, o DARF (código 6015) sai com multa e juros pelo Sicalc; registre-o aqui depois de pagar."));

        foreach (var g in vendas.Where(v => v.Data.Year == ano && v.Classe is ClasseFiscal.Tesouro or ClasseFiscal.EtfRendaFixa).GroupBy(v => v.Ticker))
            lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.InformeDaCorretora, GravidadePendencia.Aviso, AbaIrpf.TributacaoExclusiva,
                $"{g.Key}: venda no ano",
                $"O IR é retido na fonte pela corretora. O rendimento vai em Tributação Exclusiva (código 06), com o valor do informe de rendimentos da {corretora.Nome}."));

        foreach (var g in proventos.Where(p => Classe(ativos[p.AtivoId]) is ClasseFiscal.Bdr or ClasseFiscal.EtfRendaVariavel && p.Parcelas.Any(x => x.Data.Year == ano))
                     .GroupBy(p => p.AtivoId))
            lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.ForaDoEscopo, GravidadePendencia.Aviso, AbaIrpf.TributacaoExclusiva,
                $"{ativos[g.Key].Ticker}: provento de {Formatar(Classe(ativos[g.Key]))}",
                "Proventos de BDR e de ETF têm tratamento próprio e não entram nas fichas desta tela. Use o informe de rendimentos.", g.Key));

        // Regras da Lei 15.270/2025, a partir de 2026: só alertas
        if (ano >= 2026)
        {
            foreach (var g in proventos.Where(p => p.Tipo == TipoProvento.Dividendo)
                         .SelectMany(p => p.Parcelas.Where(x => x.Data.Year == ano).Select(x => (Fonte: Cnpj.Normalizar(ativos[p.AtivoId].Cnpj) ?? ativos[p.AtivoId].Ticker, x.Data.Month, x.Liquido)))
                         .GroupBy(x => (x.Fonte, x.Month)).Where(g => g.Sum(x => x.Liquido) > 50_000m))
                lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.Alerta, GravidadePendencia.Aviso, AbaIrpf.RendimentosIsentos,
                    $"Dividendos acima de R$ 50 mil em {NomeMes(g.Key.Month).ToLowerInvariant()} de uma mesma empresa",
                    "Desde 2026 há retenção de 10% nesse caso (Lei 15.270/2025). Confira o informe de rendimentos da empresa."));
            var total = isentos.Concat(exclusivos).Sum(r => r.Valor);
            if (total > 600_000m)
                lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.Alerta, GravidadePendencia.Aviso, AbaIrpf.Conferencia,
                    "Rendimentos acima de R$ 600 mil no ano",
                    "A partir de 2026 pode haver o imposto mínimo (IRPFM, Lei 15.270/2025). Procure orientação."));
        }

        lista.Add(new PendenciaIrpf(TipoPendenciaIrpf.Lembrete, GravidadePendencia.Informacao, AbaIrpf.Conferencia,
            "O que esta tela não cobre",
            "Contas bancárias, CDB/RDB, fundos de investimento fora da bolsa e dividendos de BDR: use os informes de rendimentos das instituições."));
    }

    // ── Auxiliares ──

    private static ClasseFiscal Classe(Ativo a) => TabelaIrpf.Classe(a.Tipo, a.Categoria);

    private static string Formatar(ClasseFiscal c) => c switch
    {
        ClasseFiscal.Bdr => "BDR",
        ClasseFiscal.EtfRendaVariavel => "ETF",
        _ => c.ToString()
    };

    private static bool Zero(decimal quantidade) => Math.Abs(quantidade) < 0.000001m;

    private static string? FormatarCnpj(string? cnpj) => Cnpj.Normalizar(cnpj) is string d ? Cnpj.Formatar(d) : null;

    private static string Quantidade(decimal q) => q.ToString("0.####", PtBR);

    private static string Preco(decimal p) => p.ToString(p < 1 ? "0.0000" : "N2", PtBR);

    private static string NomeMes(int mes) => PtBR.TextInfo.ToTitleCase(PtBR.DateTimeFormat.GetMonthName(mes));

    /// <summary>Maiúsculas sem acento, como na discriminação da declaração anterior.</summary>
    private static string Maiusculas(string texto)
    {
        var decomposto = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static MesRendaVariavelResponse ParaResponse(ApuracaoRendaVariavel.Mes m) => new(
        m.MesDoAno, ParaResponse(m.Comum), m.DayTrade is null ? null : ParaResponse(m.DayTrade),
        m.AlienacoesAcoes, m.AcoesIsentas, m.GanhoIsentoAcoes, m.IrrfMes + m.IrrfDayTradeMes, m.IrrfCompensado, m.IrrfACompensar,
        m.ImpostoAPagar, m.AcumuladoAnterior, m.ARecolher, m.ImpostoPago, m.DarfPendente, m.TemMovimento,
        m.Vendas.OrderBy(v => v.Data).ThenBy(v => v.Ticker)
            .Select(v => new VendaIrpfResponse(v.Ticker, v.Classe, v.Data, v.Quantidade, RegrasIrpf.Centavos(v.ValorAlienacao),
                RegrasIrpf.Centavos(v.ValorVenda), RegrasIrpf.Centavos(v.Custo), RegrasIrpf.Centavos(v.Resultado), v.DayTrade))
            .ToList());

    private static ColunaRendaVariavelResponse ParaResponse(ApuracaoRendaVariavel.Coluna c) =>
        new(c.Resultado, c.PrejuizoAnterior, c.BaseCalculo, c.PrejuizoACompensar, c.Aliquota, c.ImpostoDevido);

    private static DarfResponse ParaResponse(DarfPago d) => new(d.Id, d.Ano, d.Mes, d.Ficha, d.ValorPrincipal, d.DataPagamento);
}

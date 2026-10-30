using Wal.Domain.Enums;
using Wal.Services.Dtos;
using Wal.Services.Irpf;

namespace Wal.Services.ImportacaoB3;

/// <summary>
/// Casa os créditos de proventos do extrato da B3 com os proventos do app.
///
/// O Yahoo grava um provento por data ex, com a data ex no lugar do pagamento, tudo como "Dividendo" e somando
/// numa linha só o que a empresa pagou com a mesma data ex (dividendo + JCP + atualização Selic). A B3 traz o que
/// caiu na conta: tipo, data e valor líquido de cada parcela. Para cada provento do app (das datas ex mais
/// antigas para as mais novas — quem declarou antes recebe antes) procura-se a combinação de créditos que soma o
/// valor esperado, entre os créditos que:
///  - têm a quantidade com direito igual à registrada na véspera da data ex (diferença menor que 1 ação: a B3
///    mostra frações), depois de levar a da B3 — ações da época — para a base das movimentações do app;
///  - foram pagos entre a data ex e 12 meses depois;
///  - no JCP, têm a alíquota de IR (inferida de valor líquido ÷ bruto) compatível com o ano da data com.
/// A comparação é pelo valor bruto, com tolerância de 1,5% (a atualização Selic de pagamentos atrasados passa
/// de 0,8%), de R$ 0,0006 por ação (preço com 3 casas) e no mínimo R$ 0,02.
/// </summary>
internal static class ConciliacaoProventosB3
{
    /// <param name="Quantidade">A da B3: ações da época, antes de desdobramentos posteriores.</param>
    /// <param name="FatorRegistro">Leva a quantidade da B3 na data do crédito para a base das movimentações do app.</param>
    public record Credito(
        int Linha, int AtivoId, string Ticker, DateOnly Data, TipoProvento Tipo, decimal Quantidade,
        decimal? PrecoUnitario, decimal ValorLiquido, decimal FatorRegistro = 1);

    public record ParcelaExistente(DateOnly Data, decimal ValorBruto, decimal ValorLiquido);

    /// <param name="QuantidadeComDireito">Registrada na véspera da data ex, na base das movimentações do app.</param>
    /// <param name="ValorBruto">O que o app espera receber: para o Yahoo, o valor do MotorCarteira (base ajustada).</param>
    /// <param name="ValorBrutoSemAjuste">Yahoo: quantidade registrada × valor por ação, para quando o Yahoo não ajustou o valor.</param>
    /// <param name="FatorRegistro">
    /// Leva a quantidade da B3 na data com para a base das movimentações: diferente de 1 quando um desdobramento
    /// posterior foi lançado direto na base nova (a B3 mostra as ações da época).
    /// </param>
    public record ProventoApp(
        int Id, int AtivoId, OrigemDado Origem, TipoProvento Tipo, DateOnly DataEx, DateOnly DataCom,
        decimal QuantidadeComDireito, decimal ValorBruto, decimal? ValorBrutoSemAjuste,
        IReadOnlyList<ParcelaExistente> Parcelas, decimal FatorRegistro = 1);

    public record CreditoAvaliado(Credito Credito, decimal Aliquota, decimal ValorBruto);

    public record NovoProvento(TipoProvento Tipo, DateOnly? DataCom, decimal Quantidade, decimal ValorPorAcao, IReadOnlyList<CreditoAvaliado> Parcelas);

    /// <param name="ValorEsperado">Valor bruto que o app esperava (total do provento).</param>
    public record Item(
        AcaoImportacaoB3 Acao, int AtivoId, string Ticker, ProventoApp? Provento, decimal? ValorEsperado,
        IReadOnlyList<CreditoAvaliado> Creditos, IReadOnlyList<NovoProvento> Novos, string Detalhe)
    {
        public bool Aplicavel => Acao is AcaoImportacaoB3.Substituir or AcaoImportacaoB3.Parcial or AcaoImportacaoB3.AdicionarParcela or AcaoImportacaoB3.Criar or AcaoImportacaoB3.OcultarDuplicado;
        public decimal ValorRecebidoBruto => Creditos.Sum(c => c.ValorBruto);
    }

    private const int MesesJanela = 12;
    private const decimal ToleranciaRelativa = 0.015m;
    private const decimal ToleranciaPorAcao = 0.0006m;
    private const decimal ToleranciaMinima = 0.02m;
    // Arredondamentos do extrato: preço unitário em até 3 casas, valor em centavos
    private const decimal MeioCentesimo = 0.0005m;
    private const decimal MeioCentavo = 0.005m;
    // Busca de combinações: limite de créditos candidatos por provento
    private const int MaximoCandidatos = 20;

    public static decimal Tolerancia(decimal valor, decimal quantidade) =>
        Math.Max(ToleranciaMinima, Math.Max(ToleranciaPorAcao * quantidade, ToleranciaRelativa * Math.Abs(valor)));

    public static List<Item> Conciliar(
        IReadOnlyList<Credito> creditos, IReadOnlyList<ProventoApp> proventos, DateOnly inicioExtrato, DateOnly fimExtrato)
    {
        var itens = new List<Item>();
        foreach (var doAtivo in creditos.GroupBy(c => c.AtivoId))
            itens.AddRange(ConciliarAtivo(doAtivo.ToList(), proventos.Where(p => p.AtivoId == doAtivo.Key).ToList(), inicioExtrato, fimExtrato));

        // Proventos de ativos sem nenhum crédito no extrato
        var comCredito = creditos.Select(c => c.AtivoId).ToHashSet();
        foreach (var p in proventos.Where(p => !comCredito.Contains(p.AtivoId)))
            if (SemCredito(p, inicioExtrato, fimExtrato) is Item item) itens.Add(item);

        return itens;
    }

    private static List<Item> ConciliarAtivo(
        List<Credito> creditos, List<ProventoApp> proventos, DateOnly inicioExtrato, DateOnly fimExtrato)
    {
        var itens = new List<Item>();
        var ticker = creditos[0].Ticker;
        var restantes = creditos.OrderBy(c => c.Data).ThenBy(c => c.Linha).ToList();

        // 1. Já importados: cada parcela gravada confirma um crédito igual (mesma data, tipo e valor)
        foreach (var b3 in proventos.Where(p => p.Origem == OrigemDado.ExtratoB3))
        {
            var confirmados = new List<CreditoAvaliado>();
            foreach (var parcela in b3.Parcelas)
            {
                var credito = restantes.FirstOrDefault(c =>
                    c.Data == parcela.Data && c.Tipo == b3.Tipo && Math.Abs(c.ValorLiquido - parcela.ValorLiquido) < 0.005m);
                if (credito is null) continue;
                restantes.Remove(credito);
                confirmados.Add(new CreditoAvaliado(credito, AliquotaDoTipo(credito.Tipo, b3.DataCom), parcela.ValorBruto));
            }
            if (confirmados.Count > 0)
                itens.Add(new Item(AcaoImportacaoB3.Confirmado, b3.AtivoId, ticker, b3, b3.ValorBruto, confirmados, [], "Já importado."));
        }

        // 2. Cada provento do app fica com a combinação de créditos que fecha o valor. Do mais novo para o mais
        //    antigo: o pagamento costuma vir semanas depois da data ex, e um provento antigo (já pago antes do
        //    extrato) não pode ficar com os créditos de um recente. Primeiro todos tentam o valor ajustado; o sem
        //    ajuste (Yahoo que ainda não aplicou uma bonificação recente) só numa segunda rodada, com o que sobrou —
        //    com bonificação todo ano, o valor sem ajuste de um provento coincide com a soma de créditos de outros
        //    (ITSA4 2023). Parcelados ficam para depois, para não tomarem créditos de quem fecha o valor exato.
        var resolvidos = new HashSet<int>();
        foreach (var semAjuste in new[] { false, true })
        foreach (var p in proventos.Where(p => !resolvidos.Contains(p.Id)).OrderByDescending(p => p.DataEx).ThenByDescending(p => p.Id))
        {
            var (elegiveis, totais) = Elegiveis(p, restantes);
            var indice = semAjuste ? 1 : 0;
            if (elegiveis.Count == 0 || totais.Count <= indice ||
                MelhorCombinacao(elegiveis, totais[indice], p.QuantidadeComDireito) is not { } achou) continue;

            foreach (var c in achou.Creditos) restantes.Remove(c.Credito);
            resolvidos.Add(p.Id);
            itens.Add(p.Origem == OrigemDado.ExtratoB3
                ? new Item(AcaoImportacaoB3.AdicionarParcela, p.AtivoId, ticker, p, p.ValorBruto, achou.Creditos, [], "Última parcela do provento.")
                : new Item(AcaoImportacaoB3.Substituir, p.AtivoId, ticker, p, achou.Total, achou.Creditos,
                    NovosProventos(p, achou.Creditos, totalDeclarado: null), Detalhe(p, achou.Total, achou.Creditos)));
        }

        // Pagamento parcelado em andamento: créditos de um único tipo, em ordem, sem passar do total
        foreach (var p in proventos.Where(p => !resolvidos.Contains(p.Id) && p.DataEx.AddMonths(MesesJanela) > fimExtrato)
                     .OrderBy(p => p.DataEx).ThenBy(p => p.Id))
        {
            var (elegiveis, totais) = Elegiveis(p, restantes);
            if (elegiveis.Count == 0 || Parcelas(elegiveis, totais[0], p.QuantidadeComDireito) is not { Count: > 0 } parcelas) continue;
            // Créditos do mesmo tipo no mesmo dia são um pagamento só: se algum ficaria de fora, o valor apenas não
            // fecha (ex.: três dividendos da LEVE3 em 26/05/2023) e não é parcelamento
            var datas = parcelas.Select(c => c.Credito.Data).ToHashSet();
            if (elegiveis.Any(c => !parcelas.Contains(c) && c.Credito.Tipo == parcelas[0].Credito.Tipo && datas.Contains(c.Credito.Data))) continue;

            foreach (var c in parcelas) restantes.Remove(c.Credito);
            resolvidos.Add(p.Id);
            var falta = totais[0] - parcelas.Sum(c => c.ValorBruto);
            itens.Add(p.Origem == OrigemDado.ExtratoB3
                ? new Item(AcaoImportacaoB3.AdicionarParcela, p.AtivoId, ticker, p, p.ValorBruto, parcelas, [],
                    $"Nova parcela; ainda faltam {Moeda(falta)} (bruto).")
                : new Item(AcaoImportacaoB3.Parcial, p.AtivoId, ticker, p, p.ValorBruto, parcelas,
                    NovosProventos(p, parcelas, totalDeclarado: p.ValorBruto),
                    $"Pagamento parcelado: recebido {Moeda(parcelas.Sum(c => c.ValorBruto))} de {Moeda(p.ValorBruto)} (bruto); faltam {Moeda(falta)}."));
        }
        var semPar = proventos.Where(p => !resolvidos.Contains(p.Id)).OrderBy(p => p.DataEx).ToList();

        // 3. Créditos que sobraram: com algum provento do app que poderia ser o deles (mesma janela; a quantidade
        //    pode diferir, ex.: cota subscrita que ainda não tinha virado cota na data com), o valor ou a quantidade
        //    não fecham — criar um novo duplicaria o provento; sem nenhum, o app não conhece o provento
        foreach (var c in restantes)
        {
            var possiveis = semPar.Where(p => p.Origem != OrigemDado.ExtratoB3 && Elegivel(c, p, ignorarAliquota: true, ignorarQuantidade: true)).ToList();
            if (possiveis.Count > 0)
            {
                var provavel = possiveis[^1];
                var avaliado = Avaliar(c, provavel);
                var quantidadeB3 = c.Quantidade * provavel.FatorRegistro;
                itens.Add(new Item(AcaoImportacaoB3.Ambiguo, c.AtivoId, ticker, provavel, provavel.ValorBruto, [avaliado], [],
                    $"O crédito não fecha com o provento do app de data ex {provavel.DataEx:dd/MM/yyyy} " +
                    $"(esperado {Moeda(provavel.ValorBruto)}, crédito {Moeda(avaliado.ValorBruto)} bruto" +
                    (Math.Abs(quantidadeB3 - provavel.QuantidadeComDireito) >= 1
                        ? $"; a B3 pagou sobre {quantidadeB3:0.##} ações e o app tem {provavel.QuantidadeComDireito:0.##}"
                        : "") +
                    "). Corrija à mão."));
                continue;
            }

            var aliquota = AliquotaInferida(c);
            if (c.Tipo == TipoProvento.JCP && aliquota is null)
            {
                itens.Add(new Item(AcaoImportacaoB3.Ambiguo, c.AtivoId, ticker, null, null, [new CreditoAvaliado(c, 0, c.ValorLiquido)], [],
                    "JCP sem provento no app e sem como saber a alíquota de IR pelo extrato. Lance à mão."));
                continue;
            }
            var credito = new CreditoAvaliado(c, aliquota ?? 0, Bruto(c.ValorLiquido, aliquota ?? 0));
            var quantidade = c.Quantidade * c.FatorRegistro;
            itens.Add(new Item(AcaoImportacaoB3.Criar, c.AtivoId, ticker, null, null, [credito],
                [new NovoProvento(c.Tipo, null, quantidade, credito.ValorBruto / quantidade, [credito])],
                c.Tipo == TipoProvento.JCP
                    ? $"Não existe no app. Fica sem data com: informe-a depois (sem ela o JCP não entra no IR). O IR de {aliquota:P1} indica crédito {(aliquota == 0.15m ? "até 2025" : "a partir de 2026")}."
                    : "Não existe no app. Fica sem data com (informe-a depois, para a rentabilidade)."));
        }

        // 4. Proventos do app que sobraram: repetidos de um já trazido da B3 (importação anterior, data ex a até 3
        //    dias e mesmo valor), ou sem crédito no período
        var jaDaB3 = proventos.Where(p => p.Origem == OrigemDado.ExtratoB3).ToList();
        foreach (var p in semPar.Where(p => p.Origem != OrigemDado.ExtratoB3))
        {
            if (itens.Any(i => i.Provento?.Id == p.Id)) continue;
            var mesmoEvento = jaDaB3.Where(b => Math.Abs(b.DataEx.DayNumber - p.DataEx.DayNumber) <= 3).Sum(b => b.ValorBruto);
            if (p.Origem == OrigemDado.YahooFinance && mesmoEvento > 0
                && Math.Abs(mesmoEvento - p.ValorBruto) <= Tolerancia(p.ValorBruto, p.QuantidadeComDireito))
            {
                itens.Add(new Item(AcaoImportacaoB3.OcultarDuplicado, p.AtivoId, ticker, p, p.ValorBruto, [], [],
                    "Repete um provento que já veio do extrato da B3."));
                continue;
            }
            if (SemCredito(p, inicioExtrato, fimExtrato) is Item item) itens.Add(item);
        }

        return itens;
    }

    private static Item? SemCredito(ProventoApp p, DateOnly inicio, DateOnly fim) =>
        p.Origem != OrigemDado.ExtratoB3 && p.QuantidadeComDireito > 0 && p.DataEx >= inicio && p.DataEx <= fim
            ? new Item(AcaoImportacaoB3.SemCreditoNaB3, p.AtivoId, string.Empty, p, p.ValorBruto, [], [],
                p.DataEx.AddMonths(6) > fim
                    ? "Sem crédito na B3 até o fim do extrato — provavelmente ainda vai ser pago."
                    : "Sem crédito na B3 no período do extrato. Confira se foi pago.")
            : null;

    // Créditos que podem ser deste provento e quanto ele ainda espera receber (com e sem o ajuste do Yahoo)
    private static (List<CreditoAvaliado> Elegiveis, List<decimal> Totais) Elegiveis(ProventoApp p, List<Credito> restantes)
    {
        var jaRecebido = p.Parcelas.Sum(x => x.ValorBruto);
        // Provento da B3 já todo pago não espera mais nada
        if (p.ValorBruto - jaRecebido <= Tolerancia(p.ValorBruto, p.QuantidadeComDireito)) return ([], [0]);
        var elegiveis = restantes.Where(c => Elegivel(c, p)).Take(MaximoCandidatos).Select(c => Avaliar(c, p)).ToList();
        var totais = new List<decimal> { p.ValorBruto - jaRecebido };
        if (p.ValorBrutoSemAjuste is decimal semAjuste && semAjuste != p.ValorBruto) totais.Add(semAjuste - jaRecebido);
        return (elegiveis, totais);
    }

    private static bool Elegivel(Credito c, ProventoApp p, bool ignorarAliquota = false, bool ignorarQuantidade = false)
    {
        if (c.Data < p.DataEx || c.Data > p.DataEx.AddMonths(MesesJanela)) return false;
        if (!ignorarQuantidade && Math.Abs(c.Quantidade * p.FatorRegistro - p.QuantidadeComDireito) >= 1) return false;
        // Um provento da B3 só recebe parcelas do próprio tipo; o do Yahoo (e o manual) podem esconder vários tipos
        if (p.Origem == OrigemDado.ExtratoB3 && c.Tipo != p.Tipo) return false;
        if (ignorarAliquota || c.Tipo != TipoProvento.JCP) return true;
        return AliquotaCompativel(c, RegrasIrpf.AliquotaJcp(p.DataCom));
    }

    private static CreditoAvaliado Avaliar(Credito c, ProventoApp p)
    {
        var aliquota = AliquotaDoTipo(c.Tipo, p.DataCom);
        return new CreditoAvaliado(c, aliquota, Bruto(c.ValorLiquido, aliquota));
    }

    private static decimal AliquotaDoTipo(TipoProvento tipo, DateOnly dataCom) =>
        tipo == TipoProvento.JCP ? RegrasIrpf.AliquotaJcp(dataCom) : 0;

    private static decimal Bruto(decimal liquido, decimal aliquota) => RegrasIrpf.Centavos(liquido / (1 - aliquota));

    /// <summary>
    /// A fração líquido ÷ bruto do JCP tem de conter 1 − alíquota, considerando os arredondamentos do extrato: o
    /// preço unitário vem com 3 casas (2 nos extratos antigos) e o líquido, em centavos. Sem preço, não há como conferir.
    /// </summary>
    internal static bool AliquotaCompativel(Credito c, decimal aliquota)
    {
        if (c.PrecoUnitario is not decimal preco || preco <= 0 || c.Quantidade <= 0) return true;
        var meiaUnidade = MeiaUnidadeDaUltimaCasa(preco);
        var minimo = (c.ValorLiquido - MeioCentavo) / (c.Quantidade * (preco + meiaUnidade));
        var maximo = preco > meiaUnidade ? (c.ValorLiquido + MeioCentavo) / (c.Quantidade * (preco - meiaUnidade)) : decimal.MaxValue;
        var fracao = 1 - aliquota;
        return fracao >= minimo - 0.005m && fracao <= maximo + 0.005m;
    }

    // 0,17 → 0,005; 0,175 → 0,0005 (no mínimo 3 casas: a B3 nunca mostra mais que isso)
    private static decimal MeiaUnidadeDaUltimaCasa(decimal preco)
    {
        for (var meia = 0.5m; meia > MeioCentesimo; meia /= 10)
            if (preco % (meia * 2) == 0) return meia;
        return MeioCentesimo;
    }

    /// <summary>A alíquota do JCP quando só uma das possíveis (15% ou 17,5%) é compatível com o extrato.</summary>
    internal static decimal? AliquotaInferida(Credito c)
    {
        if (c.Tipo != TipoProvento.JCP) return 0;
        var possiveis = new[] { 0.15m, 0.175m }.Where(a => AliquotaCompativel(c, a)).ToList();
        return possiveis.Count == 1 ? possiveis[0] : null;
    }

    private record Combinacao(List<CreditoAvaliado> Creditos, decimal Total, decimal Diferenca);

    // A combinação mais próxima do total, dentro da tolerância; empate: menos créditos, depois os mais antigos
    private static Combinacao? MelhorCombinacao(List<CreditoAvaliado> elegiveis, decimal total, decimal quantidade)
    {
        Combinacao? melhor = null;
        var tolerancia = Tolerancia(total, quantidade);
        var atual = new List<CreditoAvaliado>();

        void Buscar(int inicio, decimal soma)
        {
            if (atual.Count > 0)
            {
                var diferenca = Math.Abs(soma - total);
                if (diferenca <= tolerancia && (melhor is null || diferenca < melhor.Diferenca - 0.001m ||
                                                (Math.Abs(diferenca - melhor.Diferenca) <= 0.001m && atual.Count < melhor.Creditos.Count)))
                    melhor = new Combinacao([.. atual], total, diferenca);
            }
            for (var i = inicio; i < elegiveis.Count; i++)
            {
                var nova = soma + elegiveis[i].ValorBruto;
                if (nova > total + tolerancia) continue; // valores positivos: passar do total não tem volta
                atual.Add(elegiveis[i]);
                Buscar(i + 1, nova);
                atual.RemoveAt(atual.Count - 1);
            }
        }

        Buscar(0, 0);
        return melhor;
    }

    // Parcelas já pagas de um pagamento parcelado: o tipo do primeiro crédito, em ordem de data, sem passar do total
    private static List<CreditoAvaliado> Parcelas(List<CreditoAvaliado> elegiveis, decimal total, decimal quantidade)
    {
        var tipo = elegiveis.First(c => c.Credito.Tipo != TipoProvento.Rendimento || elegiveis.All(e => e.Credito.Tipo == TipoProvento.Rendimento)).Credito.Tipo;
        var escolhidos = new List<CreditoAvaliado>();
        var soma = 0m;
        foreach (var c in elegiveis.Where(c => c.Credito.Tipo == tipo))
        {
            if (soma + c.ValorBruto > total - Tolerancia(total, quantidade)) break;
            soma += c.ValorBruto;
            escolhidos.Add(c);
        }
        return escolhidos;
    }

    /// <summary>
    /// Um provento da B3 por tipo, com a data com do provento do app e a quantidade da B3 na base do app. No
    /// parcelado, o valor por ação é o total declarado (o que falta receber continua contando).
    /// </summary>
    private static List<NovoProvento> NovosProventos(ProventoApp p, IReadOnlyList<CreditoAvaliado> creditos, decimal? totalDeclarado)
    {
        var porTipo = creditos.GroupBy(c => c.Credito.Tipo).ToList();
        return porTipo.Select(g =>
        {
            var quantidade = g.First().Credito.Quantidade * p.FatorRegistro;
            var bruto = g.Sum(c => c.ValorBruto);
            if (totalDeclarado is decimal total)
                bruto = total - creditos.Where(c => c.Credito.Tipo != g.Key).Sum(c => c.ValorBruto);
            return new NovoProvento(g.Key, p.DataCom, quantidade, bruto / quantidade, g.OrderBy(c => c.Credito.Data).ToList());
        }).ToList();
    }

    private static string Detalhe(ProventoApp p, decimal esperado, IReadOnlyList<CreditoAvaliado> creditos)
    {
        var tipos = string.Join(" + ", creditos.Select(c => c.Credito.Tipo).Distinct().Select(NomeTipo));
        var datas = string.Join(", ", creditos.Select(c => c.Credito.Data).Distinct().Select(d => d.ToString("dd/MM/yyyy")));
        var diferenca = creditos.Sum(c => c.ValorBruto) - esperado;
        return $"{tipos} pago em {datas}" + (Math.Abs(diferenca) >= 0.01m ? $" (diferença de {Moeda(diferenca)} no bruto)." : ".");
    }

    private static string NomeTipo(TipoProvento tipo) => tipo switch
    {
        TipoProvento.JCP => "JCP",
        TipoProvento.Rendimento => "Rendimento",
        _ => "Dividendo"
    };

    private static string Moeda(decimal valor) => valor.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}

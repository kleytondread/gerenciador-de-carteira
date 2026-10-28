using Wal.Domain.Entities;
using Wal.Domain.Enums;

namespace Wal.Services;

/// <summary>
/// Coloca as quantidades das movimentações na mesma base de preço do Yahoo.
///
/// O Yahoo ajusta todo o histórico de cotações e proventos por desdobramentos, grupamentos e
/// bonificações posteriores (ex.: depois da bonificação de 40% da EGIE3 em 2025, a cotação de 2022
/// aparece dividida por 1,4). Já as movimentações guardam o que foi negociado de fato. Sem converter,
/// uma compra parece perder 29% no próprio dia e a diferença só "volta" anos depois.
///
/// Cada evento do ativo é classificado pela posição registrada logo antes dele:
///  - sem posição: as movimentações anteriores estão em ações da época → convertidas;
///  - com uma compra registrada que é a própria bonificação ((posição + compra) / posição ≈ proporção):
///    as anteriores são convertidas e a compra deixa de contar como aporte — vira só o ajuste da
///    fração vendida em leilão;
///  - bonificação que daria menos de 1 ação inteira: anteriores convertidas e a fração sai como venda;
///  - com posição e sem compra correspondente: as quantidades já foram lançadas na base nova
///    (ex.: desdobramento da BBAS3 ajustado na importação) → nada a converter.
/// Movimentações e preço médio no banco não mudam; a conversão só existe dentro do cálculo.
/// </summary>
internal sealed class ConversaoBaseYahoo
{
    // Janela para achar a compra que representa a bonificação: corretoras creditam as ações
    // alguns dias depois da data ex, e às vezes o lançamento vem com a data do anúncio.
    private const int DiasAntesDoEvento = 10;
    private const int DiasDepoisDoEvento = 30;
    private const decimal ToleranciaProporcao = 0.03m;

    private readonly Dictionary<int, List<EventoCorporativo>> eventosConvertidosPorAtivo;

    /// <summary>Movimentações que são a bonificação em si (não são aporte).</summary>
    public HashSet<int> Bonificacoes { get; } = [];

    /// <summary>
    /// Diferença entre a posição convertida e a registrada logo após o evento, em ações da base
    /// da data: negativa é a fração vendida em leilão (vira dinheiro na data).
    /// </summary>
    public List<AjusteFracao> AjustesFracao { get; } = [];

    public record AjusteFracao(int AtivoId, DateOnly Data, decimal Quantidade);

    private ConversaoBaseYahoo(Dictionary<int, List<EventoCorporativo>> eventosConvertidos) =>
        eventosConvertidosPorAtivo = eventosConvertidos;

    /// <summary>
    /// Multiplicador que leva uma quantidade registrada na <paramref name="data"/> para a base do Yahoo:
    /// produto dos eventos convertidos com data ex posterior.
    /// </summary>
    public decimal Fator(int ativoId, DateOnly data)
    {
        if (!eventosConvertidosPorAtivo.TryGetValue(ativoId, out var eventos)) return 1;
        var fator = 1m;
        foreach (var e in eventos)
            if (e.Data > data) fator *= e.Fator;
        return fator;
    }

    public static ConversaoBaseYahoo Criar(IReadOnlyList<Movimentacao> movimentacoes, IEnumerable<EventoCorporativo> eventos)
    {
        var convertidos = new Dictionary<int, List<EventoCorporativo>>();
        var conversao = new ConversaoBaseYahoo(convertidos);

        // Só eventos do Yahoo: são eles que ajustam as cotações usadas na avaliação
        foreach (var grupo in eventos.Where(e => e.Origem == OrigemDado.YahooFinance).GroupBy(e => e.AtivoId))
        {
            var doAtivo = movimentacoes.Where(m => m.AtivoId == grupo.Key).ToList();

            foreach (var evento in grupo.OrderBy(e => e.Data))
            {
                var proporcao = evento.Fator;
                var posicao = PosicaoAntes(doAtivo, evento.Data, ignorarId: null);

                if (posicao <= 0)
                {
                    Converter(convertidos, evento);
                    continue;
                }

                var bonificacao = AcharBonificacao(doAtivo, evento.Data, proporcao);
                if (bonificacao is not null)
                {
                    var posicaoSemBonificacao = PosicaoAntes(doAtivo, evento.Data, ignorarId: bonificacao.Id);
                    conversao.Bonificacoes.Add(bonificacao.Id);
                    // Posição registrada depois - posição convertida = fração que não virou ação
                    var diferenca = bonificacao.Quantidade - posicaoSemBonificacao * (proporcao - 1);
                    // Nunca antes da data ex: o ajuste está em ações da base nova
                    var dataAjuste = bonificacao.Data > evento.Data ? bonificacao.Data : evento.Data;
                    if (diferenca != 0)
                        conversao.AjustesFracao.Add(new AjusteFracao(grupo.Key, dataAjuste, diferenca));
                    Converter(convertidos, evento);
                    continue;
                }

                var acoesNovas = posicao * (proporcao - 1);
                if (proporcao > 1 && Math.Floor(acoesNovas) < 1)
                {
                    // Bonificação pequena demais para dar uma ação inteira: tudo vira fração vendida
                    conversao.AjustesFracao.Add(new AjusteFracao(grupo.Key, evento.Data, -acoesNovas));
                    Converter(convertidos, evento);
                }
                // Senão: a posição já foi lançada na base nova; nada a converter
            }
        }

        return conversao;
    }

    private static void Converter(Dictionary<int, List<EventoCorporativo>> convertidos, EventoCorporativo evento)
    {
        if (!convertidos.TryGetValue(evento.AtivoId, out var lista))
            convertidos[evento.AtivoId] = lista = [];
        lista.Add(evento);
    }

    private static decimal PosicaoAntes(List<Movimentacao> doAtivo, DateOnly data, int? ignorarId) =>
        doAtivo.Where(m => m.Data < data && m.Id != ignorarId)
            .Sum(m => m.Tipo == TipoMovimentacao.Compra ? m.Quantidade : -m.Quantidade);

    private static Movimentacao? AcharBonificacao(List<Movimentacao> doAtivo, DateOnly dataEx, decimal proporcao)
    {
        foreach (var candidata in doAtivo.Where(m =>
                     m.Tipo == TipoMovimentacao.Compra &&
                     m.Data >= dataEx.AddDays(-DiasAntesDoEvento) &&
                     m.Data <= dataEx.AddDays(DiasDepoisDoEvento)))
        {
            var posicao = PosicaoAntes(doAtivo, dataEx, ignorarId: candidata.Id);
            if (posicao > 0 && Math.Abs((posicao + candidata.Quantidade) / posicao - proporcao) < ToleranciaProporcao)
                return candidata;
        }
        return null;
    }
}

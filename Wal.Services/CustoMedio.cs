using Wal.Domain.Entities;
using Wal.Domain.Enums;

namespace Wal.Services;

/// <summary>
/// Preço médio ponderado de um ativo a partir das movimentações em ordem (data, id). Uma venda não muda o
/// preço médio; uma compra depois de zerar começa do preço dela.
///
/// Na apuração fiscal (Receita, Perguntas e Respostas IRPF 2026, perguntas 705, 708 e 719):
///  - as taxas entram no custo da compra e saem do valor da venda;
///  - compra e venda do mesmo ativo no mesmo dia são day trade até a menor das duas quantidades, apuradas
///    pelos preços médios do dia, e não entram no preço médio das operações comuns.
/// Fora dela (telas de posição), o preço médio é o das compras sem taxas, como sempre foi.
/// </summary>
internal static class CustoMedio
{
    /// <param name="ValorAlienacao">Quantidade × preço, antes das taxas: é o que conta no limite de isenção de R$ 20 mil.</param>
    /// <param name="ValorVenda">Na apuração fiscal, já sem as taxas da venda.</param>
    /// <param name="Custo">Quantidade × preço médio (comum) ou custo das compras do dia (day trade).</param>
    public record Venda(DateOnly Data, decimal Quantidade, decimal ValorAlienacao, decimal ValorVenda, decimal Custo, bool DayTrade)
    {
        public decimal Resultado => ValorVenda - Custo;
    }

    public record Resultado(decimal Quantidade, decimal PrecoMedio, IReadOnlyList<Venda> Vendas)
    {
        public decimal CustoTotal => Quantidade * PrecoMedio;
    }

    public static Resultado Calcular(IEnumerable<Movimentacao> movimentacoes, bool fiscal)
    {
        decimal quantidade = 0, precoMedio = 0;
        var vendas = new List<Venda>();

        void Comprar(decimal qtd, decimal valor)
        {
            var nova = quantidade + qtd;
            precoMedio = nova == 0 ? 0 : (quantidade * precoMedio + valor) / nova;
            quantidade = nova;
        }

        void Vender(DateOnly data, decimal qtd, decimal alienacao, decimal valor)
        {
            vendas.Add(new Venda(data, qtd, alienacao, valor, qtd * precoMedio, DayTrade: false));
            quantidade -= qtd;
        }

        if (!fiscal)
        {
            foreach (var m in movimentacoes)
            {
                if (m.Tipo == TipoMovimentacao.Compra) Comprar(m.Quantidade, m.Quantidade * m.PrecoUnitario);
                else Vender(m.Data, m.Quantidade, m.Quantidade * m.PrecoUnitario, m.Quantidade * m.PrecoUnitario);
            }
            return new Resultado(quantidade, precoMedio, vendas);
        }

        foreach (var dia in movimentacoes.GroupBy(m => m.Data))
        {
            var compras = dia.Where(m => m.Tipo == TipoMovimentacao.Compra).ToList();
            var vendasDia = dia.Where(m => m.Tipo == TipoMovimentacao.Venda).ToList();
            var qtdCompra = compras.Sum(m => m.Quantidade);
            var qtdVenda = vendasDia.Sum(m => m.Quantidade);
            var valorCompra = compras.Sum(m => m.Quantidade * m.PrecoUnitario + m.Taxas);
            var alienacao = vendasDia.Sum(m => m.Quantidade * m.PrecoUnitario);
            var valorVenda = alienacao - vendasDia.Sum(m => m.Taxas);

            var dayTrade = Math.Min(qtdCompra, qtdVenda);
            if (dayTrade > 0)
            {
                var custoDayTrade = valorCompra * dayTrade / qtdCompra;
                var vendaDayTrade = valorVenda * dayTrade / qtdVenda;
                var alienacaoDayTrade = alienacao * dayTrade / qtdVenda;
                vendas.Add(new Venda(dia.Key, dayTrade, alienacaoDayTrade, vendaDayTrade, custoDayTrade, DayTrade: true));
                valorCompra -= custoDayTrade;
                valorVenda -= vendaDayTrade;
                alienacao -= alienacaoDayTrade;
                qtdCompra -= dayTrade;
                qtdVenda -= dayTrade;
            }

            // Depois do day trade sobra só compra ou só venda no dia
            if (qtdCompra > 0) Comprar(qtdCompra, valorCompra);
            if (qtdVenda > 0) Vender(dia.Key, qtdVenda, alienacao, valorVenda);
        }

        return new Resultado(quantidade, precoMedio, vendas);
    }
}

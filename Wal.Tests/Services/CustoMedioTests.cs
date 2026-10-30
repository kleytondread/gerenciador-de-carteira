using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.Services;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class CustoMedioTests
{
    private static DateOnly D(int mes, int dia) => new(2025, mes, dia);

    private static Movimentacao Compra(DateOnly data, decimal qtd, decimal preco, decimal taxas = 0) =>
        Fixtures.NovaMovimentacao(1, TipoMovimentacao.Compra, data, qtd, preco, taxas);

    private static Movimentacao Venda(DateOnly data, decimal qtd, decimal preco, decimal taxas = 0) =>
        Fixtures.NovaMovimentacao(1, TipoMovimentacao.Venda, data, qtd, preco, taxas);

    [Fact]
    public void Fiscal_TaxasEntramNoCustoESaemDaVenda()
    {
        var r = CustoMedio.Calcular([Compra(D(1, 2), 100, 10, taxas: 5), Venda(D(2, 3), 50, 12, taxas: 3)], fiscal: true);

        Assert.Equal(10.05m, r.PrecoMedio);
        Assert.Equal(50, r.Quantidade);
        var venda = Assert.Single(r.Vendas);
        Assert.Equal(600m, venda.ValorAlienacao);
        Assert.Equal(597m, venda.ValorVenda);
        Assert.Equal(502.5m, venda.Custo);
        Assert.Equal(94.5m, venda.Resultado);
        Assert.False(venda.DayTrade);
    }

    [Fact]
    public void ForaDaApuracaoFiscal_IgnoraTaxas()
    {
        var r = CustoMedio.Calcular([Compra(D(1, 2), 100, 10, taxas: 5)], fiscal: false);

        Assert.Equal(10m, r.PrecoMedio);
    }

    [Fact]
    public void Fiscal_CompraEVendaNoMesmoDia_SaoDayTradeEForaDoPrecoMedio()
    {
        var r = CustoMedio.Calcular(
            [Compra(D(1, 2), 100, 10), Compra(D(1, 3), 50, 11), Venda(D(1, 3), 80, 12)], fiscal: true);

        var dayTrade = Assert.Single(r.Vendas, v => v.DayTrade);
        Assert.Equal(50, dayTrade.Quantidade);
        Assert.Equal(550m, dayTrade.Custo);
        Assert.Equal(600m, dayTrade.ValorVenda);

        var comum = Assert.Single(r.Vendas, v => !v.DayTrade);
        Assert.Equal(30, comum.Quantidade);
        Assert.Equal(300m, comum.Custo); // preço médio das operações comuns (10), sem a compra do day trade
        Assert.Equal(360m, comum.ValorVenda);

        Assert.Equal(70, r.Quantidade);
        Assert.Equal(10m, r.PrecoMedio);
    }

    [Fact]
    public void RecompraDepoisDeZerar_ComecaDoPrecoDaCompra()
    {
        var r = CustoMedio.Calcular([Compra(D(1, 2), 10, 10), Venda(D(2, 2), 10, 12), Compra(D(3, 2), 5, 20)], fiscal: true);

        Assert.Equal(5, r.Quantidade);
        Assert.Equal(20m, r.PrecoMedio);
    }
}

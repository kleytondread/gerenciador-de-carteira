using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.Domain.Entities;
using Wal.Domain.Enums;
using Wal.ExternalApis;
using Wal.Services;
using Wal.Services.Dtos;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class AnaliseCarteiraServiceTests : SqliteInMemoryTestBase
{
    private readonly AnaliseCarteiraService sut;

    // Janeiro/2026: dia 2 é sexta, 5 é segunda
    private static DateOnly D(int mes, int dia) => new(2026, mes, dia);

    public AnaliseCarteiraServiceTests()
    {
        var indices = new IndicesReferenciaService(
            Db, Mock.Of<IBancoCentralClient>(), Mock.Of<IYahooFinanceClient>(), NullLogger<IndicesReferenciaService>.Instance);
        sut = new AnaliseCarteiraService(Db, new CarteiraService(Db, new PosicaoService(Db)), indices);
    }

    private async Task<Ativo> AtivoAsync(string ticker)
    {
        var ativo = Fixtures.NovoAtivo(ticker, ticker + " SA");
        Db.Ativos.Add(ativo);
        await Db.SaveChangesAsync();
        return ativo;
    }

    private async Task CotacoesAsync(Ativo ativo, params (DateOnly Data, decimal Preco)[] cotacoes)
    {
        Db.CotacoesHistoricas.AddRange(cotacoes.Select(c => Fixtures.NovaCotacao(ativo.Id, c.Data, c.Preco)));
        await Db.SaveChangesAsync();
    }

    private async Task MovimentarAsync(Ativo ativo, TipoMovimentacao tipo, DateOnly data, decimal quantidade, decimal preco)
    {
        Db.Movimentacoes.Add(Fixtures.NovaMovimentacao(ativo.Id, tipo, data, quantidade, preco));
        await Db.SaveChangesAsync();
    }

    // Compra 10 a 40 em 02/01; 44 → 33 (com provento de 1 na data ex 06/01) → 44, vendendo 5 em 07/01.
    // Cota: 1 → 1,1 → 0,85 → 1,1333. Resultado: 220 (valor) + 220 (venda) + 10 (provento) − 400 = 50.
    private async Task CenarioComQuedaEVendaAsync()
    {
        var ativo = await AtivoAsync("PETR4");
        await MovimentarAsync(ativo, TipoMovimentacao.Compra, D(1, 2), 10, 40);
        await MovimentarAsync(ativo, TipoMovimentacao.Venda, D(1, 7), 5, 44);
        await CotacoesAsync(ativo, (D(1, 2), 40), (D(1, 5), 44), (D(1, 6), 33), (D(1, 7), 44));
        Db.Proventos.Add(Fixtures.NovoProvento(ativo.Id, D(1, 6), 1m));
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ObterResumoPeriodosAsync_DesdeOInicio_RentabilidadePorCotasEResultadoComFluxos()
    {
        await CenarioComQuedaEVendaAsync();

        var resumo = await sut.ObterResumoPeriodosAsync();

        var inicio = resumo.Single(r => r.Periodo == PeriodoResumo.Inicio);
        Assert.Equal(D(1, 2), inicio.Inicio);
        Assert.Equal(D(1, 7), inicio.Fim);
        Assert.Equal(0.1333, (double)inicio.Rentabilidade, 4);
        Assert.Equal(50m, inicio.Resultado);
        Assert.Equal(-0.2273, (double)inicio.DrawdownMaximo, 4); // 0,85 / 1,1 − 1
        Assert.NotNull(inicio.Volatilidade);
        Assert.Null(inicio.Cdi); // sem dados de CDI no banco

        // Carteira mais nova que o mês: o mês é o próprio início
        Assert.Equal(inicio.Rentabilidade, resumo.Single(r => r.Periodo == PeriodoResumo.Mes).Rentabilidade);
    }

    [Fact]
    public async Task ObterResumoPeriodosAsync_ComCdi_CalculaPercentualDoCdiESharpe()
    {
        await CenarioComQuedaEVendaAsync();
        foreach (var dia in new[] { D(1, 2), D(1, 5), D(1, 6) })
            Db.CotacoesIndices.Add(new CotacaoIndice { Indice = IndiceReferencia.CDI, Data = dia, Valor = 0.001m });
        await Db.SaveChangesAsync();

        var inicio = (await sut.ObterResumoPeriodosAsync()).Single(r => r.Periodo == PeriodoResumo.Inicio);

        var cdiEsperado = 1.001m * 1.001m * 1.001m - 1;
        Assert.Equal(cdiEsperado, inicio.Cdi);
        Assert.Equal(inicio.Rentabilidade / cdiEsperado, inicio.PercentualCdi);
        Assert.NotNull(inicio.Sharpe);
    }

    [Fact]
    public async Task ObterResultadoMensalAsync_MesesCompostosDaoATotal()
    {
        var ativo = await AtivoAsync("VALE3");
        await MovimentarAsync(ativo, TipoMovimentacao.Compra, D(1, 29), 10, 10);
        await CotacoesAsync(ativo, (D(1, 29), 10), (D(1, 30), 11), (D(2, 2), 13.2m));

        var meses = await sut.ObterResultadoMensalAsync();

        Assert.Equal(2, meses.Count);
        Assert.Equal((2026, 1), (meses[0].Ano, meses[0].Mes));
        Assert.Equal(0.1m, meses[0].Rentabilidade);
        Assert.Equal(10m, meses[0].Resultado);
        Assert.Equal(0.2m, meses[1].Rentabilidade);
        Assert.Equal(22m, meses[1].Resultado);
    }

    [Fact]
    public async Task ObterDrawdownAsync_MaiorQuedaDesdeOPico()
    {
        await CenarioComQuedaEVendaAsync();

        var drawdown = await sut.ObterDrawdownAsync();

        Assert.Equal(-0.2273, (double)drawdown.Maximo, 4);
        Assert.Equal(D(1, 6), drawdown.DataMaximo);
        Assert.Equal(0m, drawdown.Pontos[^1].Drawdown); // 1,1333 é pico novo
    }

    [Fact]
    public async Task ObterContribuicaoPorAtivoAsync_VariacaoDeCadaAtivoNoIntervalo()
    {
        var a = await AtivoAsync("ITSA4");
        var b = await AtivoAsync("EGIE3");
        await MovimentarAsync(a, TipoMovimentacao.Compra, D(1, 2), 10, 10);
        await MovimentarAsync(b, TipoMovimentacao.Compra, D(1, 2), 10, 20);
        await CotacoesAsync(a, (D(1, 2), 10), (D(1, 5), 12), (D(1, 6), 15));
        await CotacoesAsync(b, (D(1, 2), 20), (D(1, 5), 18), (D(1, 6), 18));

        // Do dia 5 em diante: base é o dia 4 (A 100, B 200)
        var contribuicao = await sut.ObterContribuicaoPorAtivoAsync(D(1, 5), D(1, 31));

        Assert.Equal(["ITSA4", "EGIE3"], contribuicao.Select(c => c.Ticker));
        Assert.Equal(50m, contribuicao[0].Resultado);
        Assert.Equal(-20m, contribuicao[1].Resultado);
    }

    [Fact]
    public async Task ObterPosicoesEncerradasAsync_ResultadoRealizadoDosAtivosZerados()
    {
        var vendido = await AtivoAsync("WEGE3");
        var mantido = await AtivoAsync("BBSE3");
        await MovimentarAsync(vendido, TipoMovimentacao.Compra, D(1, 2), 10, 10);
        await MovimentarAsync(vendido, TipoMovimentacao.Venda, D(1, 6), 10, 15);
        await MovimentarAsync(mantido, TipoMovimentacao.Compra, D(1, 2), 1, 30);
        await CotacoesAsync(vendido, (D(1, 2), 10), (D(1, 6), 15));
        await CotacoesAsync(mantido, (D(1, 2), 30), (D(1, 6), 30));

        var encerrada = Assert.Single(await sut.ObterPosicoesEncerradasAsync());

        Assert.Equal("WEGE3", encerrada.Ticker);
        Assert.Equal(100m, encerrada.Investido);
        Assert.Equal(150m, encerrada.Vendido);
        Assert.Equal(50m, encerrada.Resultado);
        Assert.Equal(0.5m, encerrada.ResultadoPercentual);
        Assert.Equal(D(1, 2), encerrada.PrimeiraCompra);
        Assert.Equal(D(1, 6), encerrada.UltimaVenda);
    }

    // A: 10 ações desde 02/01, JCP do Yahoo de 0,50 (data ex 06/01) e dividendo manual de 1,00 com data com
    // 05/01 pago em 20/02. B: dividendo de 1,00 na data ex 05/01 e vendido por inteiro em 06/01.
    private async Task CenarioProventosAsync()
    {
        var a = await AtivoAsync("ITSA4");
        var b = await AtivoAsync("VALE3");
        await MovimentarAsync(a, TipoMovimentacao.Compra, D(1, 2), 10, 10);
        await MovimentarAsync(b, TipoMovimentacao.Compra, D(1, 2), 10, 20);
        await MovimentarAsync(b, TipoMovimentacao.Venda, D(1, 6), 10, 20);
        await CotacoesAsync(a, (D(1, 2), 10), (D(1, 6), 12));
        await CotacoesAsync(b, (D(1, 2), 20), (D(1, 6), 20));
        Db.Proventos.AddRange(
            Fixtures.NovoProvento(a.Id, D(1, 6), 0.5m, TipoProvento.JCP),
            new Provento
            {
                AtivoId = a.Id, TipoProvento = TipoProvento.Dividendo, Origem = OrigemDado.Manual,
                DataCom = D(1, 5), DataPagamento = D(2, 20), ValorPorAcao = 1m
            },
            Fixtures.NovoProvento(b.Id, D(1, 5), 1m));
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ObterProventosRecebidosAsync_QuantidadeComDireitoEDataEstimadaDoYahoo()
    {
        await CenarioProventosAsync();

        var recebidos = await sut.ObterProventosRecebidosAsync();

        Assert.Equal(3, recebidos.Count);
        var manual = recebidos[0]; // pagamento mais recente primeiro
        Assert.Equal(TipoProvento.Dividendo, manual.Tipo);
        Assert.Equal(D(2, 20), manual.DataPagamento);
        Assert.False(manual.DataPagamentoEstimada);
        Assert.Equal(10m, manual.Quantidade);
        Assert.Equal(10m, manual.Valor);
        var jcp = recebidos.Single(r => r.Tipo == TipoProvento.JCP);
        Assert.True(jcp.DataPagamentoEstimada);
        Assert.Equal(5m, jcp.Valor);
    }

    [Fact]
    public async Task ObterProventosPorPeriodoAsync_SeparaPorTipoEIncluiMesesVazios()
    {
        await CenarioProventosAsync();

        var meses = await sut.ObterProventosPorPeriodoAsync(D(1, 1), D(3, 31), AgrupamentoPeriodo.Mes);

        Assert.Equal([D(1, 1), D(2, 1), D(3, 1)], meses.Select(m => m.Inicio));
        Assert.Equal(5m, meses[0].Jcp);
        Assert.Equal(10m, meses[0].Dividendos); // B, pago na data ex
        Assert.Equal(10m, meses[1].Dividendos); // manual de A
        Assert.Equal(0m, meses[2].Total);

        var anos = await sut.ObterProventosPorPeriodoAsync(D(1, 1), D(12, 31), AgrupamentoPeriodo.Ano);
        Assert.Equal(25m, Assert.Single(anos).Total);
    }

    [Fact]
    public async Task ObterPagadoresAsync_PercentualDoTotalEAtivoEncerradoSemDy()
    {
        await CenarioProventosAsync();

        var pagadores = await sut.ObterPagadoresAsync(D(1, 1), D(12, 31));

        Assert.Equal(["ITSA4", "VALE3"], pagadores.Select(p => p.Ticker));
        Assert.Equal(15m, pagadores[0].Recebido);
        Assert.Equal(0.6m, pagadores[0].Percentual);
        Assert.False(pagadores[0].Encerrada);
        // DY e YoC de 12 meses só contam o que já foi pago até a última cotação (o JCP de 5)
        Assert.Equal(5m / 120m, pagadores[0].DividendYield12Meses);
        Assert.Equal(5m / 100m, pagadores[0].YieldOnCost12Meses);
        Assert.True(pagadores[1].Encerrada);
        Assert.Null(pagadores[1].DividendYield12Meses);
    }

    [Fact]
    public async Task ObterPosicoesDetalhadasAsync_ResultadoTotalIncluiProventos()
    {
        await CenarioProventosAsync();

        var posicao = Assert.Single(await sut.ObterPosicoesDetalhadasAsync());

        Assert.Equal("ITSA4", posicao.Ticker);
        Assert.Equal(100m, posicao.Custo);
        Assert.Equal(0.2m, posicao.Valorizacao);
        Assert.Equal(15m, posicao.ProventosRecebidos);
        Assert.Equal(35m, posicao.ResultadoTotal); // 120 − 100 + 15
        Assert.Equal(0.35m, posicao.ResultadoTotalPercentual);
        Assert.Equal(1m, posicao.Peso);
    }

    [Fact]
    public async Task CarteiraVazia_RetornaListasVazias()
    {
        Assert.Empty(await sut.ObterResumoPeriodosAsync());
        Assert.Empty(await sut.ObterResultadoMensalAsync());
        Assert.Empty((await sut.ObterDrawdownAsync()).Pontos);
        Assert.Empty(await sut.ObterContribuicaoPorAtivoAsync(D(1, 1), D(1, 31)));
        Assert.Empty(await sut.ObterPagadoresAsync(D(1, 1), D(1, 31)));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.ExternalApis;
using Wal.ExternalApis.Dtos;
using Wal.Services;
using Wal.Services.TesouroDireto;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class DadosMercadoServiceTests : SqliteInMemoryTestBase
{
    private readonly Mock<IYahooFinanceClient> yahooClientMock = new();
    private readonly Mock<ITesouroDiretoService> tesouroMock = new();
    private readonly DadosMercadoService sut;
    private readonly int ativoId;

    public DadosMercadoServiceTests()
    {
        sut = new DadosMercadoService(Db, yahooClientMock.Object, tesouroMock.Object, NullLogger<DadosMercadoService>.Instance);

        var ativo = Fixtures.NovoAtivo("PETR4", "Petrobras PN");
        Db.Ativos.Add(ativo);
        Db.SaveChanges();
        ativoId = ativo.Id;
    }

    private static YahooQuoteResult ResultadoComUmaCotacaoEUmProvento(long dataUnix, decimal fechamento, decimal provento) => new()
    {
        Symbol = "PETR4",
        HistoricalDataPrice =
        [
            new YahooHistoricalPrice { Date = dataUnix, Open = fechamento, High = fechamento, Low = fechamento, Close = fechamento, Volume = 1000 }
        ],
        Dividends = [new YahooDividend { Date = dataUnix, Amount = provento }]
    };

    [Fact]
    public async Task CargaHistoricaCompletaAsync_AtivoInexistente_NaoChamaClienteExterno()
    {
        await sut.CargaHistoricaCompletaAsync(999);

        yahooClientMock.Verify(c => c.ObterDadosAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task CargaHistoricaCompletaAsync_PersisteCotacoesEProventosMapeadosDaResposta()
    {
        var dataUnix = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        yahooClientMock
            .Setup(c => c.ObterDadosAsync("PETR4", It.IsAny<long>()))
            .ReturnsAsync(ResultadoComUmaCotacaoEUmProvento(dataUnix, fechamento: 35.5m, provento: 1.2m));

        await sut.CargaHistoricaCompletaAsync(ativoId);

        var cotacao = Assert.Single(await Db.CotacoesHistoricas.ToListAsync());
        Assert.Equal(new DateOnly(2026, 1, 10), cotacao.Data);
        Assert.Equal(35.5m, cotacao.Fechamento);

        var provento = Assert.Single(await Db.Proventos.ToListAsync());
        Assert.Equal(new DateOnly(2026, 1, 10), provento.DataPagamento);
        Assert.Equal(1.2m, provento.ValorPorAcao);
    }

    [Fact]
    public async Task CargaHistoricaCompletaAsync_ChamadaDuasVezes_NaoDuplicaRegistros()
    {
        var dataUnix = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        yahooClientMock
            .Setup(c => c.ObterDadosAsync("PETR4", It.IsAny<long>()))
            .ReturnsAsync(ResultadoComUmaCotacaoEUmProvento(dataUnix, fechamento: 35.5m, provento: 1.2m));

        await sut.CargaHistoricaCompletaAsync(ativoId);
        await sut.CargaHistoricaCompletaAsync(ativoId); // segunda chamada, mesmos dados

        Assert.Single(await Db.CotacoesHistoricas.ToListAsync());
        Assert.Single(await Db.Proventos.ToListAsync());
    }

    [Fact]
    public async Task CargaHistoricaCompletaAsync_ChamadaDuasVezes_SegundaChamadaAtualizaValores()
    {
        var dataUnix = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        yahooClientMock
            .SetupSequence(c => c.ObterDadosAsync("PETR4", It.IsAny<long>()))
            .ReturnsAsync(ResultadoComUmaCotacaoEUmProvento(dataUnix, fechamento: 35.5m, provento: 1.2m))
            .ReturnsAsync(ResultadoComUmaCotacaoEUmProvento(dataUnix, fechamento: 40.0m, provento: 1.2m)); // fechamento revisado

        await sut.CargaHistoricaCompletaAsync(ativoId);
        await sut.CargaHistoricaCompletaAsync(ativoId);

        var cotacao = Assert.Single(await Db.CotacoesHistoricas.ToListAsync());
        Assert.Equal(40.0m, cotacao.Fechamento);
    }

    [Fact]
    public async Task CargaHistoricaCompletaAsync_TimestampInvalido_IgnoraRegistroSemQuebrarOJob()
    {
        yahooClientMock
            .Setup(c => c.ObterDadosAsync("PETR4", It.IsAny<long>()))
            .ReturnsAsync(new YahooQuoteResult
            {
                Symbol = "PETR4",
                HistoricalDataPrice = [new YahooHistoricalPrice { Date = long.MaxValue, Close = 10 }],
                Dividends = []
            });

        await sut.CargaHistoricaCompletaAsync(ativoId); // não deve lançar

        Assert.Empty(await Db.CotacoesHistoricas.ToListAsync());
    }

    [Fact]
    public async Task SincronizarAtivoAsync_FalhaDoClienteExterno_PropagaExcecao()
    {
        yahooClientMock
            .Setup(c => c.ObterDadosAsync("PETR4", It.IsAny<long>()))
            .ThrowsAsync(new HttpRequestException("Yahoo Finance indisponível"));

        // O job diário (Wal.Jobs.AtivoSyncJob) depende dessa exceção propagar para poder isolar
        // a falha por ativo dentro do loop (ver Fase 5.4) — se o serviço engolisse o erro aqui,
        // o job não teria como diferenciar sucesso de falha por ativo.
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SincronizarAtivoAsync(ativoId));
    }

    [Fact]
    public async Task SincronizarAtivoAsync_AtivoInexistente_NaoLancaENaoChamaClienteExterno()
    {
        await sut.SincronizarAtivoAsync(999);

        yahooClientMock.Verify(c => c.ObterDadosAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task CargaHistoricaCompletaAsync_PersisteEventosCorporativosSemDuplicar()
    {
        var dataUnix = new DateTimeOffset(2025, 11, 27, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var resultado = ResultadoComUmaCotacaoEUmProvento(dataUnix, fechamento: 30m, provento: 1m);
        resultado.Splits = [new YahooSplit { Date = dataUnix, Numerator = 3.5m, Denominator = 2.5m }];
        yahooClientMock.Setup(c => c.ObterDadosAsync("PETR4", It.IsAny<long>())).ReturnsAsync(resultado);

        await sut.CargaHistoricaCompletaAsync(ativoId);
        await sut.SincronizarAtivoAsync(ativoId); // o mesmo evento volta na sincronização

        var evento = Assert.Single(await Db.EventosCorporativos.ToListAsync());
        Assert.Equal(new DateOnly(2025, 11, 27), evento.Data);
        Assert.Equal(1.4m, evento.Fator);
        Assert.Equal(Wal.Domain.Enums.OrigemDado.YahooFinance, evento.Origem);
    }

    [Fact]
    public async Task SincronizarAtivoAsync_Tesouro_GravaPrecosDoArquivoSemChamarYahoo()
    {
        var titulo = Fixtures.NovoAtivo("IPCA+ 2045", "Tesouro IPCA+ 2045", Wal.Domain.Enums.TipoAtivo.Tesouro);
        titulo.TituloTesouro = "Tesouro IPCA+";
        titulo.VencimentoTesouro = new DateOnly(2045, 5, 15);
        Db.Ativos.Add(titulo);
        await Db.SaveChangesAsync();
        tesouroMock
            .Setup(t => t.ObterPrecosAsync("Tesouro IPCA+", new DateOnly(2045, 5, 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(new DateOnly(2022, 12, 2), 1050m), (new DateOnly(2026, 10, 23), 1457.55m)]);

        await sut.SincronizarAtivoAsync(titulo.Id);
        await sut.CargaHistoricaCompletaAsync(titulo.Id); // de novo: não duplica

        var cotacoes = await Db.CotacoesHistoricas.Where(c => c.AtivoId == titulo.Id).OrderBy(c => c.Data).ToListAsync();
        Assert.Equal(2, cotacoes.Count);
        Assert.Equal(1457.55m, cotacoes[1].Fechamento);
        Assert.Equal(0, cotacoes[1].Volume);
        yahooClientMock.Verify(c => c.ObterDadosAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task SincronizarAtivoAsync_TesouroSemPrecos_LancaParaOJobRegistrarAFalha()
    {
        var titulo = Fixtures.NovoAtivo("SELIC 2029", "Tesouro Selic 2029", Wal.Domain.Enums.TipoAtivo.Tesouro);
        titulo.TituloTesouro = "Tesouro Selic";
        titulo.VencimentoTesouro = new DateOnly(2029, 3, 1);
        Db.Ativos.Add(titulo);
        await Db.SaveChangesAsync();
        tesouroMock
            .Setup(t => t.ObterPrecosAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SincronizarAtivoAsync(titulo.Id));
    }

    [Fact]
    public async Task CargaHistoricaCompletaAsync_NaoSobrescreveProventoManualDaMesmaData()
    {
        var dataUnix = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Db.Proventos.Add(Fixtures.NovoProvento(ativoId, new DateOnly(2026, 1, 10), valorPorAcao: 0.05m,
            origem: Wal.Domain.Enums.OrigemDado.Manual));
        await Db.SaveChangesAsync();
        yahooClientMock
            .Setup(c => c.ObterDadosAsync("PETR4", It.IsAny<long>()))
            .ReturnsAsync(ResultadoComUmaCotacaoEUmProvento(dataUnix, fechamento: 35.5m, provento: 1.2m));

        await sut.CargaHistoricaCompletaAsync(ativoId);

        var proventos = await Db.Proventos.OrderBy(p => p.Origem).ToListAsync();
        Assert.Equal(2, proventos.Count);
        Assert.Equal(1.2m, proventos[0].ValorPorAcao);   // Yahoo
        Assert.Equal(0.05m, proventos[1].ValorPorAcao);  // manual intacto
    }
}

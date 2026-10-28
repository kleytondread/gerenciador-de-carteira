using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.Domain.Enums;
using Wal.ExternalApis;
using Wal.ExternalApis.Dtos;
using Wal.Services;
using Wal.Tests.TestHelpers;

namespace Wal.Tests.Services;

public class IndicesReferenciaServiceTests : SqliteInMemoryTestBase
{
    private readonly Mock<IBancoCentralClient> bancoCentral = new();
    private readonly Mock<IYahooFinanceClient> yahoo = new();
    private readonly IndicesReferenciaService sut;

    private static readonly DateOnly Dia = new(2026, 1, 2);
    private static readonly long DiaUnix = new DateTimeOffset(2026, 1, 2, 13, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public IndicesReferenciaServiceTests()
    {
        bancoCentral.Setup(b => b.ObterSerieAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        yahoo.Setup(y => y.ObterDadosSimboloAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(new YahooQuoteResult());
        sut = new IndicesReferenciaService(Db, bancoCentral.Object, yahoo.Object, NullLogger<IndicesReferenciaService>.Instance);
    }

    private void Sgs(int codigo, decimal valor) =>
        bancoCentral.Setup(b => b.ObterSerieAsync(codigo, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ValorSerieSgs(Dia, valor)]);

    private void Yahoo(string simbolo, decimal fechamento, decimal? ajustado = null) =>
        yahoo.Setup(y => y.ObterDadosSimboloAsync(simbolo, It.IsAny<long>()))
            .ReturnsAsync(new YahooQuoteResult
            {
                HistoricalDataPrice = [new YahooHistoricalPrice { Date = DiaUnix, Close = fechamento, AdjClose = ajustado }]
            });

    private async Task<decimal?> ValorAsync(IndiceReferencia indice) =>
        await Db.CotacoesIndices.Where(c => c.Indice == indice && c.Data == Dia).Select(c => (decimal?)c.Valor).SingleOrDefaultAsync();

    [Fact]
    public async Task SincronizarAsync_GravaCadaIndiceNaUnidadeCerta()
    {
        Sgs(12, 0.05m);      // CDI em % ao dia
        Sgs(433, 0.5m);      // IPCA em % ao mês
        Sgs(1, 5.33m);       // PTAX em R$
        Yahoo("^BVSP", 175000);
        Yahoo("^GSPC", 6800);
        Yahoo("DIVO11.SA", 126, ajustado: 120);

        await sut.SincronizarAsync();

        Assert.Equal(0.0005m, await ValorAsync(IndiceReferencia.CDI));
        Assert.Equal(0.005m, await ValorAsync(IndiceReferencia.IPCA));
        Assert.Equal(5.33m, await ValorAsync(IndiceReferencia.PTAX));
        Assert.Equal(175000m, await ValorAsync(IndiceReferencia.IBOV));
        Assert.Equal(6800m, await ValorAsync(IndiceReferencia.SP500));
        Assert.Equal(120m, await ValorAsync(IndiceReferencia.IDIV)); // preço ajustado: inclui proventos
    }

    [Fact]
    public async Task SincronizarAsync_RodarDeNovo_AtualizaSemDuplicar()
    {
        Sgs(12, 0.05m);
        await sut.SincronizarAsync();
        Sgs(12, 0.06m);
        await sut.SincronizarAsync();

        Assert.Equal(1, await Db.CotacoesIndices.CountAsync(c => c.Indice == IndiceReferencia.CDI));
        Assert.Equal(0.0006m, await ValorAsync(IndiceReferencia.CDI));
    }

    [Fact]
    public async Task SincronizarAsync_FalhaNumIndice_NaoImpedeOsDemais()
    {
        bancoCentral.Setup(b => b.ObterSerieAsync(12, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("fora do ar"));
        Yahoo("^BVSP", 175000);

        await sut.SincronizarAsync();

        Assert.Null(await ValorAsync(IndiceReferencia.CDI));
        Assert.Equal(175000m, await ValorAsync(IndiceReferencia.IBOV));
    }

    [Fact]
    public async Task ObterSituacaoAsync_ListaTodosOsIndicesMesmoSemDados()
    {
        Yahoo("^BVSP", 175000);
        await sut.SincronizarAsync();

        var situacao = await sut.ObterSituacaoAsync();

        Assert.Equal(Enum.GetValues<IndiceReferencia>().Length, situacao.Count);
        var ibov = situacao.Single(s => s.Indice == IndiceReferencia.IBOV);
        Assert.Equal(Dia, ibov.UltimaData);
        Assert.Equal(175000m, ibov.UltimoValor);
        Assert.Equal(0, situacao.Single(s => s.Indice == IndiceReferencia.CDI).Quantidade);
    }
}

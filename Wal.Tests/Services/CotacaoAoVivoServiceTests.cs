using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wal.ExternalApis;
using Wal.ExternalApis.Dtos;
using Wal.Services;
using Wal.Services.Common;

namespace Wal.Tests.Services;

public class CotacaoAoVivoServiceTests
{
    private sealed class RelogioFalso : TimeProvider
    {
        public DateTimeOffset Agora { get; set; }
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private readonly RelogioFalso relogio = new();
    private readonly Mock<IYahooFinanceClient> yahoo = new();
    private readonly CotacaoAoVivoService sut;

    public CotacaoAoVivoServiceTests() =>
        sut = new CotacaoAoVivoService(yahoo.Object, new MemoryCache(new MemoryCacheOptions()), relogio, NullLogger<CotacaoAoVivoService>.Instance);

    // Quarta-feira, 28/10/2026, no horário de São Paulo
    private static DateTimeOffset Quarta(int hora, int minuto = 0) => new(2026, 10, 28, hora, minuto, 0, TimeSpan.FromHours(-3));

    private void YahooResponde(DateTimeOffset horario) =>
        yahoo.Setup(y => y.ObterCotacaoAtualAsync("PETR4")).ReturnsAsync(new YahooCotacaoAtual(38.5m, 38m, horario));

    [Theory]
    [InlineData(28, 9, 59, false)]
    [InlineData(28, 10, 0, true)]
    [InlineData(28, 17, 59, true)]
    [InlineData(28, 18, 1, false)]
    [InlineData(31, 11, 0, false)] // sábado
    public void Pregao_AbertoSoEmDiaUtilDas10As18(int dia, int hora, int minuto, bool aberto) =>
        Assert.Equal(aberto, Pregao.Aberto(new DateTimeOffset(2026, 10, dia, hora, minuto, 0, TimeSpan.FromHours(-3))));

    [Fact]
    public async Task ObterAsync_NoPregao_BuscaDeNovoSoDepoisDoIntervalo()
    {
        YahooResponde(Quarta(11));

        relogio.Agora = Quarta(11);
        var cotacoes = await sut.ObterAsync(["PETR4"]);
        relogio.Agora = Quarta(11, 9);
        await sut.ObterAsync(["PETR4"]);
        yahoo.Verify(y => y.ObterCotacaoAtualAsync("PETR4"), Times.Once);

        relogio.Agora = Quarta(11, 10);
        await sut.ObterAsync(["PETR4"]);
        yahoo.Verify(y => y.ObterCotacaoAtualAsync("PETR4"), Times.Exactly(2));
        Assert.Equal(38.5m, cotacoes["PETR4"].Preco);
    }

    [Fact]
    public async Task ObterAsync_ForaDoPregao_NaoChamaOYahooEDevolveOQueJaTinhaDeHoje()
    {
        YahooResponde(Quarta(17, 50));
        relogio.Agora = Quarta(19);
        Assert.Empty(await sut.ObterAsync(["PETR4"]));

        relogio.Agora = Quarta(17, 55);
        await sut.ObterAsync(["PETR4"]);
        relogio.Agora = Quarta(19);
        var cotacoes = await sut.ObterAsync(["PETR4"]);

        Assert.Equal(38.5m, cotacoes["PETR4"].Preco);
        yahoo.Verify(y => y.ObterCotacaoAtualAsync("PETR4"), Times.Once);
    }

    [Fact]
    public async Task ObterAsync_CotacaoDeOutroDia_EhDescartada()
    {
        // Feriado: o Yahoo devolve o último pregão
        YahooResponde(Quarta(17).AddDays(-1));
        relogio.Agora = Quarta(11);

        Assert.Empty(await sut.ObterAsync(["PETR4"]));
    }

    [Fact]
    public async Task ObterAsync_FalhaDoYahoo_DevolveVazioSemDerrubar()
    {
        yahoo.Setup(y => y.ObterCotacaoAtualAsync("PETR4")).ThrowsAsync(new HttpRequestException("fora do ar"));
        relogio.Agora = Quarta(11);

        Assert.Empty(await sut.ObterAsync(["PETR4"]));
    }
}

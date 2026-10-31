using Microsoft.Extensions.Logging.Abstractions;
using Wal.ExternalApis;
using Wal.Tests.TestHelpers;
using static Wal.Tests.TestHelpers.HandlerFalso;

namespace Wal.Tests.ExternalApis;

public class YahooFinanceClientTests
{
    private static (YahooFinanceClient Cliente, HandlerFalso Handler) Criar(string json)
    {
        var handler = new HandlerFalso(_ => Json(json));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com/v8/finance/") };
        return (new YahooFinanceClient(http, NullLogger<YahooFinanceClient>.Instance), handler);
    }

    [Fact]
    public async Task ObterCotacaoAtualAsync_LePrecoFechamentoAnteriorEHorarioDoMeta()
    {
        var (cliente, handler) = Criar("""
            {"chart":{"result":[{"meta":{"symbol":"PETR4.SA","regularMarketPrice":38.52,
             "chartPreviousClose":38.1,"regularMarketTime":1793203200}}],"error":null}}
            """);

        var cotacao = await cliente.ObterCotacaoAtualAsync("PETR4");

        Assert.NotNull(cotacao);
        Assert.Equal(38.52m, cotacao.Preco);
        Assert.Equal(38.1m, cotacao.FechamentoAnterior);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793203200), cotacao.Horario);
        Assert.Equal("/v8/finance/chart/PETR4.SA", Assert.Single(handler.Requisicoes).AbsolutePath);
        Assert.Contains("range=1d", handler.Requisicoes[0].Query);
    }

    [Fact]
    public async Task ObterCotacaoAtualAsync_ErroDoYahoo_DevolveNulo()
    {
        var (cliente, _) = Criar("""{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""");

        Assert.Null(await cliente.ObterCotacaoAtualAsync("XPTO3"));
    }

    // Caso real (30/10/2026): o último pregão veio com abertura, máxima, mínima e volume, mas fechamento nulo
    private static string GraficoComUltimoFechamentoNulo(long horarioMeta) => $$$"""
        {"chart":{"result":[{"meta":{"symbol":"BBAS3.SA","regularMarketPrice":16.47,"regularMarketTime":{{{horarioMeta}}}},
         "timestamp":[1793278800,1793365200],
         "indicators":{"quote":[{"open":[16.86,16.36],"high":[16.94,16.75],"low":[16.34,16.13],
           "close":[16.46,null],"volume":[70352700,61272900]}]}}],"error":null}}
        """;

    [Fact]
    public async Task ObterDadosAsync_UltimoFechamentoNulo_UsaPrecoDoMetaDoMesmoDia()
    {
        var (cliente, _) = Criar(GraficoComUltimoFechamentoNulo(horarioMeta: 1793390700)); // 30/10 20:05 UTC

        var dados = await cliente.ObterDadosAsync("BBAS3", 0);

        Assert.Equal(2, dados.HistoricalDataPrice.Count);
        var ultimo = dados.HistoricalDataPrice[^1];
        Assert.Equal(1793365200, ultimo.Date);
        Assert.Equal(16.47m, ultimo.Close);
        Assert.Equal(16.36m, ultimo.Open);
        Assert.Equal(61272900, ultimo.Volume);
    }

    [Fact]
    public async Task ObterDadosAsync_UltimoFechamentoNulo_MetaDeOutroDia_PulaABarra()
    {
        var (cliente, _) = Criar(GraficoComUltimoFechamentoNulo(horarioMeta: 1793304300)); // 29/10 20:05 UTC

        var dados = await cliente.ObterDadosAsync("BBAS3", 0);

        Assert.Equal(1793278800, Assert.Single(dados.HistoricalDataPrice).Date);
    }

    [Fact]
    public async Task ObterCotacaoAtualAsync_SemPreco_DevolveNulo()
    {
        var (cliente, _) = Criar("""{"chart":{"result":[{"meta":{"symbol":"PETR4.SA","chartPreviousClose":38.1}}],"error":null}}""");

        Assert.Null(await cliente.ObterCotacaoAtualAsync("PETR4"));
    }
}

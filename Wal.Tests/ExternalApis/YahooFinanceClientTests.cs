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

    [Fact]
    public async Task ObterCotacaoAtualAsync_SemPreco_DevolveNulo()
    {
        var (cliente, _) = Criar("""{"chart":{"result":[{"meta":{"symbol":"PETR4.SA","chartPreviousClose":38.1}}],"error":null}}""");

        Assert.Null(await cliente.ObterCotacaoAtualAsync("PETR4"));
    }
}

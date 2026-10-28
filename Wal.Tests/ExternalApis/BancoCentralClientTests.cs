using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Wal.ExternalApis;
using Wal.Tests.TestHelpers;
using static Wal.Tests.TestHelpers.HandlerFalso;

namespace Wal.Tests.ExternalApis;

public class BancoCentralClientTests
{
    private static (BancoCentralClient Cliente, HandlerFalso Handler) Criar(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new HandlerFalso(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.bcb.gov.br/") };
        var cliente = new BancoCentralClient(http, NullLogger<BancoCentralClient>.Instance) { EsperaEntreTentativas = TimeSpan.Zero };
        return (cliente, handler);
    }

    [Fact]
    public async Task ObterSerieAsync_LeDataBrasileiraEValorComPonto()
    {
        var (cliente, handler) = Criar(_ => Json("""[{"data":"20/10/2026","valor":"0.050788"},{"data":"21/10/2026","valor":"0.050790"}]"""));

        var valores = await cliente.ObterSerieAsync(12, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21));

        Assert.Equal(2, valores.Count);
        Assert.Equal(new ValorSerieSgs(new DateOnly(2026, 10, 20), 0.050788m), valores[0]);
        Assert.Contains("bcdata.sgs.12/dados", handler.Requisicoes[0].ToString());
        Assert.Contains("dataInicial=20/10/2026", handler.Requisicoes[0].ToString());
    }

    [Fact]
    public async Task ObterSerieAsync_SemValoresNoIntervalo_RetornaVazio()
    {
        var (cliente, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var valores = await cliente.ObterSerieAsync(433, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 27));

        Assert.Empty(valores);
    }

    [Fact]
    public async Task ObterSerieAsync_IntervaloLongo_DivideEmJanelasDeUmAno()
    {
        var (cliente, handler) = Criar(_ => Json("[]"));

        await cliente.ObterSerieAsync(12, new DateOnly(2020, 1, 1), new DateOnly(2026, 10, 27));

        Assert.Equal(7, handler.Requisicoes.Count);
        Assert.Contains("dataFinal=31/12/2020", handler.Requisicoes[0].ToString());
        Assert.Contains("dataInicial=01/01/2026", handler.Requisicoes[6].ToString());
        Assert.Contains("dataFinal=27/10/2026", handler.Requisicoes[6].ToString());
    }

    [Fact]
    public async Task ObterSerieAsync_Erro502Passageiro_TentaDeNovo()
    {
        var chamadas = 0;
        var (cliente, _) = Criar(_ => ++chamadas == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Json("""[{"data":"02/01/2026","valor":"0.05"}]"""));

        var valores = await cliente.ObterSerieAsync(12, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(2, chamadas);
        Assert.Single(valores);
    }

    [Fact]
    public async Task ObterSerieAsync_ErroQuePersiste_LancaDepoisDeTresTentativas()
    {
        var (cliente, handler) = Criar(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cliente.ObterSerieAsync(12, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        Assert.Equal(3, handler.Requisicoes.Count);
    }

    [Fact]
    public async Task ObterSerieAsync_JanelaQueVoltaErroNoCorpo_DivideAoMeio()
    {
        // Como o CDI de 2024 inteiro: status 200 com {"erro":{}}, mas cada metade funciona
        var (cliente, handler) = Criar(req => req.RequestUri!.ToString().Contains("dataInicial=01/01/2024&dataFinal=31/12/2024")
            ? Json("""{"erro":{}}""")
            : Json("""[{"data":"02/01/2024","valor":"0.043739"}]"""));

        var valores = await cliente.ObterSerieAsync(12, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        Assert.Equal(3, handler.Requisicoes.Count); // ano inteiro + duas metades
        Assert.Equal(2, valores.Count);
    }
}

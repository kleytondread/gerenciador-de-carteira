using Wal.Services;

namespace GerenciadorDeCarteira.Endpoints;

public static class CarteiraEndpoints
{
    public static void MapCarteiraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/carteira");

        group.MapGet("/posicoes", async (ICarteiraService service) =>
            Results.Ok(await service.ObterPosicoesAsync()));

        group.MapGet("/resumo", async (ICarteiraService service) =>
            Results.Ok(await service.ObterResumoAsync()));

        group.MapGet("/rentabilidade-historica", async (ICarteiraService service) =>
            Results.Ok(await service.ObterRentabilidadeHistoricaAsync()));
    }
}

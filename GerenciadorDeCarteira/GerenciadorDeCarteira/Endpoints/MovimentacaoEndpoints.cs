using Wal.Services;
using Wal.Services.Dtos;

namespace GerenciadorDeCarteira.Endpoints;

public static class MovimentacaoEndpoints
{
    public static void MapMovimentacaoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/movimentacoes");

        group.MapGet("/", async (int? ativoId, IMovimentacaoService service) =>
            Results.Ok(await service.ListarAsync(ativoId)));

        group.MapPost("/", async (CriarMovimentacaoRequest request, IMovimentacaoService service) =>
        {
            var resultado = await service.CriarAsync(request);
            return resultado.Sucesso
                ? Results.Created($"/api/movimentacoes/{resultado.Valor!.Id}", resultado.Valor)
                : resultado.ParaHttp();
        });

        group.MapPut("/{id:int}", async (int id, EditarMovimentacaoRequest request, IMovimentacaoService service) =>
        {
            var resultado = await service.EditarAsync(id, request);
            return resultado.Sucesso ? Results.Ok(resultado.Valor) : resultado.ParaHttp();
        });

        group.MapDelete("/{id:int}", async (int id, IMovimentacaoService service) =>
        {
            var resultado = await service.ExcluirAsync(id);
            return resultado.Sucesso ? Results.NoContent() : resultado.ParaHttp();
        });
    }
}

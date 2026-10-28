using Wal.Services;
using Wal.Services.Dtos;

namespace GerenciadorDeCarteira.Endpoints;

public static class AtivoEndpoints
{
    public static void MapAtivoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ativos");

        group.MapPost("/", async (CriarAtivoRequest request, IAtivoService service) =>
        {
            var resultado = await service.CriarAsync(request);
            return resultado.Sucesso
                ? Results.Created($"/api/ativos/{resultado.Valor!.Id}", resultado.Valor)
                : resultado.ParaHttp();
        });

        // incluirInativos: ausente por padrão traz todos (ativos + inativos) — decisão da Tarefa 2.2.
        group.MapGet("/", async (string? busca, bool? incluirInativos, IAtivoService service) =>
            Results.Ok(await service.ListarAsync(busca, incluirInativos ?? true)));

        group.MapGet("/{id:int}", async (int id, IAtivoService service) =>
        {
            var resultado = await service.ObterPorIdAsync(id);
            return resultado.Sucesso ? Results.Ok(resultado.Valor) : resultado.ParaHttp();
        });

        group.MapPut("/{id:int}", async (int id, EditarAtivoRequest request, IAtivoService service) =>
        {
            var resultado = await service.EditarAsync(id, request);
            return resultado.Sucesso ? Results.Ok(resultado.Valor) : resultado.ParaHttp();
        });

        group.MapDelete("/{id:int}", async (int id, IAtivoService service) =>
        {
            var resultado = await service.ExcluirAsync(id);
            return resultado.Sucesso ? Results.NoContent() : resultado.ParaHttp();
        });

        group.MapPost("/{id:int}/reativar", async (int id, IAtivoService service) =>
        {
            var resultado = await service.ReativarAsync(id);
            return resultado.Sucesso ? Results.NoContent() : resultado.ParaHttp();
        });

        group.MapGet("/{id:int}/cotacoes", async (int id, string? periodo, IAtivoService service) =>
        {
            var resultado = await service.ObterCotacoesAsync(id, periodo);
            return resultado.Sucesso ? Results.Ok(resultado.Valor) : resultado.ParaHttp();
        });
    }
}

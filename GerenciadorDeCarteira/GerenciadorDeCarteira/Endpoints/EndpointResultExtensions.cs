using Wal.Services.Common;

namespace GerenciadorDeCarteira.Endpoints;

internal static class EndpointResultExtensions
{
    public static IResult ParaHttp(this ResultadoOperacao resultado) => resultado.TipoErro switch
    {
        TipoErroOperacao.NaoEncontrado => Results.NotFound(new { erro = resultado.Erro }),
        TipoErroOperacao.Conflito => Results.Conflict(new { erro = resultado.Erro }),
        _ => Results.BadRequest(new { erro = resultado.Erro })
    };

    public static IResult ParaHttp<T>(this ResultadoOperacao<T> resultado) => resultado.TipoErro switch
    {
        TipoErroOperacao.NaoEncontrado => Results.NotFound(new { erro = resultado.Erro }),
        TipoErroOperacao.Conflito => Results.Conflict(new { erro = resultado.Erro }),
        _ => Results.BadRequest(new { erro = resultado.Erro })
    };
}

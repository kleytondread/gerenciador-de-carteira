namespace GerenciadorDeCarteira.Endpoints;

/// <summary>
/// Tratamento global de exceção não tratada restrito às rotas /api/*: loga a exceção completa
/// e responde 500 com JSON padronizado, sem expor stack trace ao cliente (Tarefa 12.2).
/// Restrito a /api de propósito — as páginas Blazor têm o próprio tratamento de erro do framework
/// (error boundary/página de erro), que não deve ser interceptado aqui.
/// Erros esperados (validação, duplicidade, not found) nunca chegam aqui: os services os devolvem
/// como <see cref="Wal.Services.Common.ResultadoOperacao"/> e os endpoints os mapeiam para 400/404/409.
/// </summary>
public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        // Erro do próprio request (ex: JSON malformado no binding do minimal API) — sem o catch
        // dedicado, cairia no genérico abaixo e viraria um 500 indevido; o status correto vem
        // na própria exceção (tipicamente 400).
        catch (BadHttpRequestException ex) when (context.Request.Path.StartsWithSegments("/api"))
        {
            logger.LogWarning("Requisição inválida em {Metodo} {Rota}: {Motivo}",
                context.Request.Method, context.Request.Path, ex.Message);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                erro = "Requisição inválida. Verifique o formato do corpo e dos parâmetros enviados."
            });
        }
        catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
        {
            logger.LogError(ex, "Exceção não tratada em {Metodo} {Rota}.",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw; // resposta já em andamento: não dá para reescrever — deixa o pipeline padrão encerrar

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                erro = "Erro interno inesperado. Detalhes foram registrados no log do servidor."
            });
        }
    }
}

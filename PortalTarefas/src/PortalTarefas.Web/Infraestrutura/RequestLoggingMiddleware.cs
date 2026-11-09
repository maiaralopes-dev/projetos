using System.Diagnostics;

namespace PortalTarefas.Web.Infraestrutura;

/// <summary>
/// Registra método, caminho, status e duração de cada requisição.
/// Não registra query string, cabeçalhos ou corpo, pois podem conter dados sensíveis.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var inicio = Stopwatch.GetTimestamp();
        logger.LogInformation("[2-Log] entrada {Metodo} {Caminho}", context.Request.Method, context.Request.Path);
        try
        {
            await next(context);
        }
        finally
        {
            var duracao = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            logger.LogInformation("[2-Log] saída {Metodo} {Caminho} -> {Status} em {DuracaoMs:F1} ms",
                context.Request.Method, context.Request.Path, context.Response.StatusCode, duracao);
        }
    }
}

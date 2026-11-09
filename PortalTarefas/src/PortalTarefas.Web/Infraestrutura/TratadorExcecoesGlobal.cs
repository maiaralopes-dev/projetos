using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PortalTarefas.Web.Infraestrutura;

/// <summary>
/// Converte exceções não tratadas em Problem Details (RFC 9457) sem expor
/// mensagem, stack trace ou tipo interno. O detalhe fica somente no log,
/// associado ao identificador de correlação.
/// </summary>
public sealed class TratadorExcecoesGlobal(IProblemDetailsService problemDetails, ILogger<TratadorExcecoesGlobal> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // Erros de binding (JSON malformado, enum inválido na query) são falhas do cliente: 400.
        if (exception is BadHttpRequestException bad)
        {
            logger.LogWarning("Requisição inválida em {Metodo} {Caminho}: {Motivo}",
                context.Request.Method, context.Request.Path, bad.Message);
            context.Response.StatusCode = bad.StatusCode;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = bad.StatusCode,
                    Title = "Requisição inválida.",
                    Detail = "Verifique o formato dos parâmetros e do corpo JSON."
                }
            });
        }

        logger.LogError(exception, "Erro não tratado em {Metodo} {Caminho}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erro interno ao processar a requisição.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                Detail = "Informe o identificador de correlação ao suporte."
            }
        });
    }
}

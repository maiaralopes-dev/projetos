namespace PortalTarefas.Web.Infraestrutura;

/// <summary>
/// Garante que toda requisição tenha um identificador de correlação.
/// Reaproveita o cabeçalho X-Correlation-Id enviado pelo cliente (se for seguro)
/// ou gera um novo, devolvendo-o na resposta e abrindo um escopo de log.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string NomeCabecalho = "X-Correlation-Id";
    public const string ChaveItem = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ObterOuGerar(context);
        context.Items[ChaveItem] = correlationId;
        context.TraceIdentifier = correlationId;

        // O cabeçalho é adicionado antes de a resposta começar a ser enviada.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[NomeCabecalho] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            logger.LogDebug("[1-Correlação] entrada {Path}", context.Request.Path);
            await next(context);
            logger.LogDebug("[1-Correlação] saída {Path}", context.Request.Path);
        }
    }

    private static string ObterOuGerar(HttpContext context)
    {
        var recebido = context.Request.Headers[NomeCabecalho].ToString();
        // Aceita somente valores curtos e alfanuméricos para evitar injeção em logs.
        if (!string.IsNullOrWhiteSpace(recebido) && recebido.Length <= 64 &&
            recebido.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            return recebido;
        }
        return Guid.NewGuid().ToString("N");
    }
}

namespace PortalTarefas.Web.Infraestrutura;

/// <summary>Extensões reutilizáveis para registrar os middlewares do projeto.</summary>
public static class PipelineExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    public static IApplicationBuilder UseRegistroRequisicoes(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();

    /// <summary>Adiciona correlationId a todo Problem Details produzido pela aplicação.</summary>
    public static IServiceCollection AddProblemDetailsComCorrelacao(this IServiceCollection services) =>
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            if (ctx.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ChaveItem, out var id))
                ctx.ProblemDetails.Extensions["correlationId"] = id;
            ctx.ProblemDetails.Instance ??= $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
        });
}

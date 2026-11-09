using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Infraestrutura;

namespace PortalTarefas.Web.Endpoints;

/// <summary>Endpoint diagnóstico em Minimal API (equivalente ao DiagnosticoController).</summary>
public static class DiagnosticoEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/minimal/diagnostico", (
                [FromHeader(Name = "X-Cliente-Id")] string? clienteId,
                HttpContext ctx,
                IWebHostEnvironment env) =>
            TypedResults.Ok(new DiagnosticoDto("MinimalAPI", env.EnvironmentName,
                ctx.Items[CorrelationIdMiddleware.ChaveItem]?.ToString() ?? "", clienteId, DateTimeOffset.UtcNow)))
            .WithTags("Diagnóstico");

        // Provoca uma exceção para demonstrar o tratamento centralizado.
        app.MapGet("/api/minimal/diagnostico/erro", string () =>
                throw new InvalidOperationException("Falha simulada ao acessar dependência interna db-interno:1433"))
            .WithTags("Diagnóstico");

        // Encerramento antecipado: ShortCircuit executa o endpoint sem passar pelos middlewares restantes.
        app.MapGet("/ping", () => "pong").ShortCircuit().AllowAnonymous();

        return app;
    }
}

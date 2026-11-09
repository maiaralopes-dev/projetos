using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PortalTarefas.Web.Seguranca;
using PortalTarefas.Web.Servicos;

namespace PortalTarefas.Web.Endpoints;

public static class OperacaoEndpoints
{
    public static IEndpointRouteBuilder MapOperacao(this IEndpointRouteBuilder app)
    {
        // Liveness: o processo responde? Não consulta dependências (reiniciar o contêiner não resolveria o banco fora).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

        // Readiness: pode receber tráfego? Verifica banco e dependência essencial. Resposta pública só com o status.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

        // Detalhes (nome de cada verificação, status e duração) somente para administradores.
        app.MapHealthChecks("/health/detalhes", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("ready"),
            ResponseWriter = (ctx, rel) => ctx.Response.WriteAsJsonAsync(new
            {
                status = rel.Status.ToString(),
                duracaoMs = rel.TotalDuration.TotalMilliseconds,
                verificacoes = rel.Entries.Select(e => new { nome = e.Key, status = e.Value.Status.ToString(), e.Value.Description, duracaoMs = e.Value.Duration.TotalMilliseconds })
            })
        }).RequireAuthorization(Politicas.ExcluirTarefas);

        // Leitura pública e não personalizada: candidata a output caching.
        var cal = app.MapGroup("/api/v2/calendario").WithTags("Calendário");
        cal.MapGet("/feriados/{ano:int}", async (int ano, CalendarioCliente cliente, CancellationToken ct) =>
            {
                if (ano is < 2000 or > 2100)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ano"] = ["Ano entre 2000 e 2100."] });
                var r = await cliente.ObterFeriadosAsync(ano, ct);
                return r.Resultado switch
                {
                    "sucesso" => Results.Ok(r.Feriados),
                    "circuito_aberto" => Results.Problem(statusCode: 503, title: "Serviço de calendário temporariamente indisponível.",
                        detail: "Proteção por circuit breaker ativa; tente novamente em instantes."),
                    "timeout" => Results.Problem(statusCode: 504, title: "Serviço de calendário não respondeu a tempo."),
                    _ => Results.Problem(statusCode: 502, title: "Falha ao consultar o serviço de calendário.")
                };
            })
            .AllowAnonymous()
            .CacheOutput("feriados")
            .RequireRateLimiting("publica")
            .WithName("ObterFeriados").WithSummary("Feriados nacionais do ano (público, em cache por 60 s).");

        cal.MapPost("/cache/invalidar", async (IOutputCacheStore cache, CancellationToken ct) =>
            {
                await cache.EvictByTagAsync("feriados", ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.ExcluirTarefas)
            .AddEndpointFilter<AntiforgeryApiFilter>()
            .WithName("InvalidarCacheFeriados").WithSummary("Invalida o cache de feriados (Administrador).");
        return app;
    }
}

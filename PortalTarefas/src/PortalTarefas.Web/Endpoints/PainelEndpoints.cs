using PortalTarefas.Web.Servicos;

namespace PortalTarefas.Web.Endpoints;

public static class PainelEndpoints
{
    public static IEndpointRouteBuilder MapPainelEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/painel").WithTags("Painel");

        // Mesma leitura implementada com EF Core e com Dapper, para comparação.
        grupo.MapGet("/ef", (PainelServico painel, TimeProvider relogio, CancellationToken ct) =>
            painel.ResumoEfAsync(DateOnly.FromDateTime(relogio.GetLocalNow().Date), ct));
        grupo.MapGet("/dapper", (PainelServico painel, TimeProvider relogio, CancellationToken ct) =>
            painel.ResumoDapperAsync(DateOnly.FromDateTime(relogio.GetLocalNow().Date), ct));

        return app;
    }

    /// <summary>Endpoints de apoio aos testes — mapeados somente em Development.</summary>
    public static IEndpointRouteBuilder MapEndpointsDesenvolvimento(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/tarefas/{id:int}/concluir", async (int id, bool? simularFalha, ITarefaServico servico, CancellationToken ct) =>
            await servico.ConcluirAsync(id, "dev", simularFalha ?? false, ct)
                ? Results.NoContent()
                : Results.Problem(statusCode: 404, title: "Tarefa não encontrada."))
            .WithTags("Desenvolvimento");

        // Medição local e simples (não é benchmark): executa N vezes cada versão após aquecimento.
        app.MapGet("/api/dev/painel/medicao", async (int? n, PainelServico painel, CancellationToken ct) =>
        {
            var vezes = Math.Clamp(n ?? 200, 10, 2000);
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            async Task<object> Medir(string nome, Func<Task> acao)
            {
                for (var i = 0; i < 20; i++) await acao();   // aquecimento (JIT, cache de plano, pool)
                var tempos = new double[vezes];
                for (var i = 0; i < vezes; i++)
                {
                    var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
                    await acao();
                    tempos[i] = System.Diagnostics.Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
                }
                Array.Sort(tempos);
                return new { versao = nome, execucoes = vezes, medianaMs = Math.Round(tempos[vezes / 2], 3),
                    p95Ms = Math.Round(tempos[(int)(vezes * 0.95)], 3), maxMs = Math.Round(tempos[^1], 3) };
            }
            return Results.Ok(new[]
            {
                await Medir("EF Core", () => painel.ResumoEfAsync(hoje, ct)),
                await Medir("Dapper", () => painel.ResumoDapperAsync(hoje, ct)),
            });
        }).WithTags("Desenvolvimento");
        return app;
    }
}

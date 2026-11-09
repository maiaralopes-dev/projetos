using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Servicos;

namespace PortalTarefas.Web.Endpoints;

/// <summary>Endpoints de tarefas com Minimal APIs, demonstrando binding de quatro fontes.</summary>
public static class TarefasEndpoints
{
    private static TarefaDto ParaDto(Tarefa t) => new(t.Id, t.Titulo, t.Descricao, t.Situacao);

    public static RouteGroupBuilder MapTarefasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/tarefas").WithTags("Tarefas (v1 - unidade 1)");

        // Versionamento: a v2 mudou o formato da listagem (array -> página), uma mudança incompatível.
        // A v1 continua respondendo, mas sinaliza a descontinuação e aponta a sucessora (RFC 9745 / RFC 8288).
        grupo.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers["Deprecation"] = "@1798761600";   // 2027-01-01T00:00:00Z
            ctx.HttpContext.Response.Headers.Link = "</api/v2/tarefas>; rel=\"successor-version\"";
            return await next(ctx);
        });

        // Filtros pela query string (?situacao=&busca=) e cliente pelo cabeçalho X-Cliente-Id (opcional).
        grupo.MapGet("/", async (
                [FromQuery] SituacaoTarefa? situacao,
                [FromQuery] string? busca,
                [FromHeader(Name = "X-Cliente-Id")] string? clienteId,
                ITarefaServico servico,
                HttpResponse response,
                CancellationToken ct) =>
            {
                if (!string.IsNullOrEmpty(clienteId))
                    response.Headers["X-Cliente-Atendido"] = clienteId;
                var tarefas = await servico.ListarAsync(situacao, busca, ct);
                return TypedResults.Ok(tarefas.Select(ParaDto));
            })
            .WithName("ListarTarefasV1");

        // Id pela rota com restrição :int — "abc" nem chega ao handler (404 por rota).
        grupo.MapGet("/{id:int}", async Task<Results<Ok<TarefaDto>, ProblemHttpResult>> (int id, ITarefaServico servico, CancellationToken ct) =>
                await servico.ObterAsync(id, ct) is { } tarefa
                    ? TypedResults.Ok(ParaDto(tarefa))
                    : TypedResults.Problem(statusCode: 404, title: "Tarefa não encontrada.",
                        detail: $"Não existe tarefa com id {id}."))
            .WithName("ObterTarefaV1");

        // Corpo JSON validado automaticamente (AddValidation do .NET 10) — 400 em caso de erro.
        grupo.MapPost("/", async Task<Created<TarefaDto>> (CriarTarefaRequest req, ITarefaServico servico, CancellationToken ct) =>
            {
                var tarefa = await servico.CriarAsync(new Tarefa
                {
                    Titulo = req.Titulo.Trim(), Descricao = req.Descricao, Situacao = req.Situacao
                }, ct);
                return TypedResults.Created($"/api/v1/tarefas/{tarefa.Id}", ParaDto(tarefa));
            })
            .WithName("CriarTarefaV1");

        return grupo;
    }
}

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Servicos;

namespace PortalTarefas.Web.Endpoints;

/// <summary>Endpoints de tarefas com Minimal APIs, demonstrando binding de quatro fontes.</summary>
public static class TarefasEndpoints
{
    public static RouteGroupBuilder MapTarefasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/tarefas").WithTags("Tarefas");

        // Filtros pela query string (?situacao=&busca=) e cliente pelo cabeçalho X-Cliente-Id (opcional).
        grupo.MapGet("/", (
                [FromQuery] SituacaoTarefa? situacao,
                [FromQuery] string? busca,
                [FromHeader(Name = "X-Cliente-Id")] string? clienteId,
                RepositorioTarefasMemoria repo,
                HttpResponse response) =>
            {
                if (!string.IsNullOrEmpty(clienteId))
                    response.Headers["X-Cliente-Atendido"] = clienteId;
                return TypedResults.Ok(repo.Listar(situacao, busca));
            })
            .WithName("ListarTarefas");

        // Id pela rota com restrição :int — "abc" nem chega ao handler (404 por rota).
        grupo.MapGet("/{id:int}", Results<Ok<TarefaDto>, ProblemHttpResult> (int id, RepositorioTarefasMemoria repo) =>
                repo.Obter(id) is { } tarefa
                    ? TypedResults.Ok(tarefa)
                    : TypedResults.Problem(statusCode: 404, title: "Tarefa não encontrada.",
                        detail: $"Não existe tarefa com id {id}."))
            .WithName("ObterTarefa");

        // Corpo JSON validado automaticamente (AddValidation do .NET 10) — 400 em caso de erro.
        grupo.MapPost("/", Created<TarefaDto> (CriarTarefaRequest req, RepositorioTarefasMemoria repo) =>
            {
                var tarefa = repo.Adicionar(req.Titulo, req.Descricao, req.Situacao);
                return TypedResults.Created($"/api/v1/tarefas/{tarefa.Id}", tarefa);
            })
            .WithName("CriarTarefa");

        return grupo;
    }
}

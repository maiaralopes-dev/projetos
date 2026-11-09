using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Seguranca;
using PortalTarefas.Web.Servicos;

namespace PortalTarefas.Web.Endpoints;

/// <summary>
/// API REST v2 de tarefas. Contrato:
///   GET    /api/v2/tarefas          200 (página) | 400
///   GET    /api/v2/tarefas/{id}     200 | 404
///   POST   /api/v2/tarefas          201 + Location | 400 | 409 (título duplicado)
///   PUT    /api/v2/tarefas/{id}     200 | 400 | 404 | 409 (versão desatualizada ou título duplicado)
///   DELETE /api/v2/tarefas/{id}     204 | 404
/// </summary>
public static class TarefasApiV2Endpoints
{
    public const string TipoConflito = "https://portaltarefas.example/problemas/conflito";

    public static RouteGroupBuilder MapTarefasApiV2(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v2/tarefas")
            .WithTags("Tarefas")
            .RequireAuthorization(Politicas.LerTarefas)          // leitura: qualquer usuário autenticado
            .AddEndpointFilter<AntiforgeryApiFilter>()           // CSRF para chamadas com cookie
            .RequireRateLimiting("api")                          // cota por usuário (429 ao exceder)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        grupo.MapGet("/", Listar).WithName("ListarTarefas")
            .WithSummary("Lista tarefas com filtros, ordenação e paginação.")
            .WithDescription("Ordenação aceita somente os campos da lista permitida. Os metadados de paginação " +
                             "também são enviados nos cabeçalhos X-Total-Count e Link (RFC 8288).");
        grupo.MapGet("/{id:int}", Obter).WithName("ObterTarefa").WithSummary("Obtém os detalhes de uma tarefa.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        grupo.MapPost("/", Criar).WithName("CriarTarefa").WithSummary("Cadastra uma tarefa (papel Usuario ou Administrador).")
            .RequireAuthorization(Politicas.CriarTarefas)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        grupo.MapPut("/{id:int}", Atualizar).WithName("AtualizarTarefa")
            .WithSummary("Substitui os dados de uma tarefa.")
            .WithDescription("Envie em 'versao' o valor recebido no GET. Se outra alteração ocorreu depois, a resposta é 409 " +
                             "com a versão atual na extensão 'versaoAtual'.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        grupo.MapDelete("/{id:int}", Excluir).WithName("ExcluirTarefa").WithSummary("Exclui uma tarefa (somente Administrador).")
            .RequireAuthorization(Politicas.ExcluirTarefas)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return grupo;
    }

    internal static async Task<Results<Ok<PaginaDto<TarefaResumoDto>>, ValidationProblem>> Listar(
        [AsParameters] ConsultaTarefasQuery consulta, ConsultaTarefasServico servico, HttpContext http, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        if (consulta.OrdenarPor is { } campo && !ConsultaTarefasQuery.CamposOrdenacao.Contains(campo, StringComparer.OrdinalIgnoreCase))
            erros["ordenarPor"] = [$"Campo de ordenação não permitido. Use: {string.Join(", ", ConsultaTarefasQuery.CamposOrdenacao)}."];
        if (consulta.Direcao is { } dir && dir is not ("asc" or "desc"))
            erros["direcao"] = ["Use 'asc' ou 'desc'."];
        if (erros.Count > 0) return TypedResults.ValidationProblem(erros, title: "Parâmetros de consulta inválidos.");

        var pagina = await servico.ConsultarAsync(consulta, ct);
        AdicionarCabecalhosPaginacao(http, pagina);
        return TypedResults.Ok(pagina);
    }

    internal static async Task<Results<Ok<TarefaDetalhesDto>, ProblemHttpResult>> Obter(int id, ITarefaServico servico, CancellationToken ct) =>
        await servico.ObterAsync(id, ct) is { } t
            ? TypedResults.Ok(ConsultaTarefasServico.ParaDetalhes(t))
            : NaoEncontrada(id);

    internal static async Task<Results<Created<TarefaDetalhesDto>, ValidationProblem, ProblemHttpResult>> Criar(
        CriarTarefaDto dto, ITarefaServico servico, TimeProvider relogio, ClaimsPrincipal usuario, CancellationToken ct)
    {
        if (ValidarRegras(dto.Prioridade, dto.Prazo, SituacaoTarefa.Pendente, relogio) is { } erro) return erro;
        if (await servico.ExisteTituloAsync(dto.Titulo, null, ct)) return TituloDuplicado(dto.Titulo);

        var criada = await servico.CriarAsync(new Tarefa
        {
            Titulo = dto.Titulo.Trim(), Descricao = dto.Descricao, Prioridade = dto.Prioridade, Prazo = dto.Prazo,
            Equipe = usuario.Equipe() ?? "Geral",                       // a tarefa pertence à equipe de quem cria
            CriadaPor = usuario.FindFirstValue(ClaimTypes.NameIdentifier)
        }, ct);
        return TypedResults.Created($"/api/v2/tarefas/{criada.Id}", ConsultaTarefasServico.ParaDetalhes(criada));
    }

    internal static async Task<Results<Ok<TarefaDetalhesDto>, ValidationProblem, ProblemHttpResult, ForbidHttpResult>> Atualizar(
        int id, AtualizarTarefaDto dto, ITarefaServico servico, TimeProvider relogio, IAuthorizationService autorizacao,
        HttpContext http, CancellationToken ct)
    {
        if (ValidarRegras(dto.Prioridade, dto.Prazo, dto.Situacao, relogio) is { } erro) return erro;
        byte[] versao;
        try { versao = Convert.FromBase64String(dto.Versao); }
        catch (FormatException) { return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["versao"] = ["Versão em formato inválido."] }); }

        if (await servico.ObterAsync(id, ct) is not { } existente) return NaoEncontrada(id);
        // Autorização por recurso: depende da equipe da tarefa, por isso é avaliada após carregá-la.
        if (!(await autorizacao.AuthorizeAsync(http.User, existente, Politicas.EditarTarefa)).Succeeded)
            return TypedResults.Forbid();
        if (await servico.ExisteTituloAsync(dto.Titulo, id, ct)) return TituloDuplicado(dto.Titulo);

        var tarefa = new Tarefa
        {
            Id = id, Titulo = dto.Titulo.Trim(), Descricao = dto.Descricao, Situacao = dto.Situacao,
            Prioridade = dto.Prioridade, Prazo = dto.Prazo, Versao = versao, Equipe = existente.Equipe
        };
        try
        {
            await servico.AtualizarAsync(tarefa, http.User.Identity?.Name, ct);
        }
        catch (ConflitoConcorrenciaException ex)
        {
            if (ex.ValoresAtuais is null) return NaoEncontrada(id);
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, type: TipoConflito,
                title: "A tarefa foi alterada por outra requisição.",
                detail: "Obtenha a tarefa novamente, reaplique suas alterações e envie a nova versão.",
                extensions: new Dictionary<string, object?> { ["versaoAtual"] = Convert.ToBase64String(ex.ValoresAtuais.Versao) });
        }
        return TypedResults.Ok(ConsultaTarefasServico.ParaDetalhes((await servico.ObterAsync(id, ct))!));
    }

    internal static async Task<Results<NoContent, ProblemHttpResult>> Excluir(int id, ITarefaServico servico, CancellationToken ct) =>
        await servico.ExcluirAsync(id, ct) ? TypedResults.NoContent() : NaoEncontrada(id);

    // ---------- auxiliares ----------

    private static ValidationProblem? ValidarRegras(PrioridadeTarefa prioridade, DateOnly? prazo, SituacaoTarefa situacao, TimeProvider relogio)
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().Date);
        if (prioridade == PrioridadeTarefa.Alta && prazo is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["prazo"] = ["Tarefas de prioridade Alta precisam de prazo."] });
        if (prazo < hoje && situacao != SituacaoTarefa.Concluida)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["prazo"] = ["O prazo não pode estar no passado."] });
        return null;
    }

    private static ProblemHttpResult NaoEncontrada(int id) =>
        TypedResults.Problem(statusCode: 404, title: "Tarefa não encontrada.", detail: $"Não existe tarefa com id {id}.");

    private static ProblemHttpResult TituloDuplicado(string titulo) =>
        TypedResults.Problem(statusCode: 409, type: TipoConflito, title: "Título já utilizado.",
            detail: $"Já existe uma tarefa com o título '{titulo.Trim()}'.");

    private static void AdicionarCabecalhosPaginacao<T>(HttpContext http, PaginaDto<T> pagina)
    {
        http.Response.Headers["X-Total-Count"] = pagina.TotalItens.ToString();
        var links = new List<string>();
        string Url(int p) => $"<{http.Request.Path}{QueryString.Create(http.Request.Query
            .Where(q => !q.Key.Equals("pagina", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => KeyValuePair.Create(q.Key, v)))
            .Append(KeyValuePair.Create("pagina", (string?)p.ToString())))}>";
        links.Add($"{Url(1)}; rel=\"first\"");
        if (pagina.Pagina > 1) links.Add($"{Url(pagina.Pagina - 1)}; rel=\"prev\"");
        if (pagina.Pagina < pagina.TotalPaginas) links.Add($"{Url(pagina.Pagina + 1)}; rel=\"next\"");
        if (pagina.TotalPaginas > 0) links.Add($"{Url(pagina.TotalPaginas)}; rel=\"last\"");
        http.Response.Headers.Link = string.Join(", ", links);
    }
}

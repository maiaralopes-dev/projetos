using HubPedidos.Application.Contratos;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Api.Http;
using HubPedidos.Application.Contratos.V2;
using HubPedidos.Processamento;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Modulos.Pedidos;

/// <summary>Endpoints de leitura e de alteração de status (atividade 3).</summary>
public static class EndpointsConsultas
{
    public static IEndpointRouteBuilder MapConsultasPedidos(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pedidos", ListarAsync).WithName("ListarPedidos");
        app.MapGet("/pedidos/cursor", (string? apos, int? tamanho, IConsultasPedidos consultas, CancellationToken ct) =>
            consultas.ListarPorCursorAsync(apos, tamanho ?? 20, ct)).WithName("ListarPedidosPorCursor");
        // Rota legada SEM versão: continua sendo a v1 (política de versão padrão).
        app.MapGet("/pedidos/{id:guid}", ObterAsync).WithName("ObterPedido").AddEndpointFilter(Versionamento.FiltroVersao(1))
            .Produces<PedidoDetalheResponse>().Produces(StatusCodes.Status304NotModified).Produces(StatusCodes.Status404NotFound);
        app.MapPut("/pedidos/{id:guid}/status", AlterarStatusAsync).WithName("AlterarStatus");
        app.MapGet("/relatorios/pedidos-itens", (int? pagina, int? tamanho, IConsultasPedidos consultas, CancellationToken ct) =>
            consultas.RelatorioComItensAsync(pagina ?? 1, tamanho ?? 20, ct)).WithName("RelatorioPedidosItens");
        // Leitura pública e idêntica para todos: candidata a output cache (tag "painel",
        // invalidada quando qualquer pedido muda). Ver Http/CachePolitica.cs.
        app.MapGet("/painel/regioes", (IConsultasPedidos consultas, CancellationToken ct) =>
            consultas.PainelPorRegiaoAsync(ct)).WithName("PainelRegioes").CacheOutput(CachePolitica.Painel);
        return app;
    }

    private static async Task<Results<Ok<PaginaResponse<PedidoResumoResponse>>, ValidationProblem>> ListarAsync(
        string? clienteId, string? regiao, string? status, int? pagina, int? tamanho,
        IConsultasPedidos consultas, CancellationToken cancellationToken)
    {
        var erros = new Dictionary<string, string[]>();
        Regiao? filtroRegiao = null;
        StatusPedido? filtroStatus = null;

        if (regiao is not null)
        {
            if (MapeadorPedidos.TentarEnum<Regiao>(regiao, out var r)) { filtroRegiao = r; }
            else { erros["regiao"] = ["Região inválida."]; }
        }

        if (status is not null)
        {
            if (MapeadorPedidos.TentarEnum<StatusPedido>(status, out var s)) { filtroStatus = s; }
            else { erros["status"] = ["Status inválido."]; }
        }

        if (erros.Count > 0)
        {
            return TypedResults.ValidationProblem(erros);
        }

        var filtro = new FiltroPedidos
        {
            ClienteId = string.IsNullOrWhiteSpace(clienteId) ? null : clienteId.Trim(),
            Regiao = filtroRegiao,
            Status = filtroStatus,
            Pagina = pagina ?? 1,
            Tamanho = tamanho ?? 20,
        };

        return TypedResults.Ok(await consultas.ListarAsync(filtro, cancellationToken));
    }

    // Rota legada = v1: mesma representação e mesma ETag de /api/v1/pedidos/{id}.
    private static async Task<IResult> ObterAsync(Guid id, IConsultasPedidos consultas, HttpContext http, CancellationToken cancellationToken) =>
        await consultas.ObterDetalheAsync(id, cancellationToken) is { } detalhe
            ? Etag.ResponderComEtag(http, detalhe)
            : TypedResults.NotFound();

    /// <summary>
    /// Extensão (atividade 7): atualização otimista com If-Match. O cliente envia a ETag da
    /// representação que viu (v1 ou v2); se o pedido mudou desde então, 412 e nada é gravado.
    /// Sem If-Match, o comportamento anterior é mantido (compatibilidade).
    /// Limitação: verificar e gravar são dois passos; uma mudança entre eles não é detectada
    /// aqui (o repositório ainda valida a transição de status).
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetailsSimples>, ProblemHttpResult>> AlterarStatusAsync(
        Guid id, AlterarStatusRequest request, IRepositorioPedidos repositorio, IConsultasPedidos consultas,
        INotificadorPedidos notificador, HttpContext http, CancellationToken cancellationToken)
    {
        if (!MapeadorPedidos.TentarEnum<StatusPedido>(request.Status, out var novo))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status inválido."] });
        }

        if (http.Request.Headers.IfMatch.Count > 0)
        {
            var atual = await consultas.ObterDetalheAsync(id, cancellationToken);
            if (atual is null)
            {
                return TypedResults.NotFound();
            }

            var etags = new[] { Etag.Calcular(Etag.Serializar(http, atual)), Etag.Calcular(Etag.Serializar(http, AdaptadorPedidoV2.De(atual))) };
            if (Etag.CasaIfMatch(http.Request, etags) == false)
            {
                return TypedResults.Problem(
                    title: "Pré-condição falhou",
                    detail: "O pedido mudou desde a sua última leitura. Consulte de novo e repita a operação.",
                    statusCode: StatusCodes.Status412PreconditionFailed);
            }
        }

        var resultado = await repositorio.AlterarStatusAsync(id, novo, cancellationToken);
        if (resultado == ResultadoAlteracaoStatus.Alterado && await consultas.ObterResumoAsync(id, cancellationToken) is { } resumo)
        {
            // Publica DEPOIS de persistir (SignalR, registro, invalidação de cache).
            await notificador.PublicarAsync(new PedidoAtualizado(id, resumo.ClienteId, novo,
                Enum.Parse<Application.Classificacao.CategoriaProcessamento>(resumo.Categoria), DateTimeOffset.UtcNow), cancellationToken);
        }

        return resultado switch
        {
            ResultadoAlteracaoStatus.Alterado => TypedResults.NoContent(),
            ResultadoAlteracaoStatus.NaoEncontrado => TypedResults.NotFound(),
            _ => TypedResults.Conflict(new ProblemDetailsSimples("Transição de status não permitida.")),
        };
    }
}

public sealed record ProblemDetailsSimples(string Detalhe);

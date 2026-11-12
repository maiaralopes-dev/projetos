using HubPedidos.Application.Contratos;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
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
        app.MapGet("/pedidos/{id:guid}", ObterAsync).WithName("ObterPedido").AddEndpointFilter(Versionamento.FiltroVersao(1));
        app.MapPut("/pedidos/{id:guid}/status", AlterarStatusAsync).WithName("AlterarStatus");
        app.MapGet("/relatorios/pedidos-itens", (int? pagina, int? tamanho, IConsultasPedidos consultas, CancellationToken ct) =>
            consultas.RelatorioComItensAsync(pagina ?? 1, tamanho ?? 20, ct)).WithName("RelatorioPedidosItens");
        app.MapGet("/painel/regioes", (IConsultasPedidos consultas, CancellationToken ct) =>
            consultas.PainelPorRegiaoAsync(ct)).WithName("PainelRegioes");
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

    private static async Task<Results<Ok<PedidoDetalheResponse>, NotFound>> ObterAsync(Guid id, IConsultasPedidos consultas, CancellationToken cancellationToken) =>
        await consultas.ObterDetalheAsync(id, cancellationToken) is { } detalhe
            ? TypedResults.Ok(detalhe)
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetailsSimples>>> AlterarStatusAsync(
        Guid id, AlterarStatusRequest request, IRepositorioPedidos repositorio, CancellationToken cancellationToken)
    {
        if (!MapeadorPedidos.TentarEnum<StatusPedido>(request.Status, out var novo))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status inválido."] });
        }

        return await repositorio.AlterarStatusAsync(id, novo, cancellationToken) switch
        {
            ResultadoAlteracaoStatus.Alterado => TypedResults.NoContent(),
            ResultadoAlteracaoStatus.NaoEncontrado => TypedResults.NotFound(),
            _ => TypedResults.Conflict(new ProblemDetailsSimples("Transição de status não permitida.")),
        };
    }
}

public sealed record ProblemDetailsSimples(string Detalhe);

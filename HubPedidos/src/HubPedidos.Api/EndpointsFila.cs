using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Application.Processadores;
using HubPedidos.Application.Processamento;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api;

/// <summary>Produtor HTTP da fila e métricas (atividade 4).</summary>
public static class EndpointsFila
{
    public static IEndpointRouteBuilder MapFila(this IEndpointRouteBuilder app)
    {
        app.MapPost("/pedidos/fila", EnfileirarAsync).WithName("EnfileirarPedido");
        app.MapGet("/fila/metricas", (FilaPedidos fila, MetricasFila m, LimitadorBanco l) => new MetricasFilaResponse(
            fila.Profundidade, fila.Capacidade, fila.ModoCheio.ToString(),
            m.Aceitos, m.RejeitadosFilaCheia, m.RejeitadosEncerrando, m.Descartados, m.Processados, m.Falhas, m.Abandonados,
            m.EmAndamento, l.Limite, l.EmUso, l.MaximoObservado, m.ProcessadosPorCategoria)).WithName("MetricasFila");
        return app;
    }

    /// <summary>
    /// Valida e classifica de forma síncrona; o processamento e a gravação ficam com os
    /// consumidores. 202 = aceito na fila (ainda não gravado); 503 = fila cheia/encerrando,
    /// sem nenhuma gravação, então o cliente pode repetir com segurança.
    /// </summary>
    private static async Task<Results<Accepted<PedidoEnfileiradoResponse>, ValidationProblem, ProblemHttpResult>> EnfileirarAsync(
        CriarPedidoRequest? request,
        CatalogoProcessadores catalogo,
        FilaPedidos fila,
        HttpResponse resposta,
        CancellationToken cancellationToken)
    {
        var resultado = MapeadorPedidos.ParaDominio(request);
        if (!resultado.Sucesso)
        {
            return TypedResults.ValidationProblem(resultado.Erros
                .GroupBy(e => e.Campo)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Mensagem).ToArray()));
        }

        var pedido = resultado.Valor!;
        var categoria = ClassificadorPedidos.Classificar(pedido);
        if (categoria is CategoriaProcessamento.Invalido || !catalogo.TentarObter(categoria, out _))
        {
            return TypedResults.Problem(title: "Pedido não processável", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        switch (await fila.EnfileirarAsync(pedido, categoria, cancellationToken))
        {
            case ResultadoEnfileiramento.Aceito:
                return TypedResults.Accepted($"/pedidos/{pedido.Id}", new PedidoEnfileiradoResponse(pedido.Id.Valor, categoria.ToString(), "Enfileirado"));

            case ResultadoEnfileiramento.RejeitadoFilaCheia:
                resposta.Headers.RetryAfter = "1";
                return TypedResults.Problem(
                    title: "Fila cheia",
                    detail: $"Capacidade de {fila.Capacidade} itens atingida. Tente novamente em instantes.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            default:
                return TypedResults.Problem(title: "Serviço encerrando", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

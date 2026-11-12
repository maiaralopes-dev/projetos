using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Processamento;
using HubPedidos.Processamento.Fila;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Modulos.Pedidos;

/// <summary>Escrita de pedidos: criação síncrona e importação de lote.</summary>
public static class EndpointsPedidos
{
    public static IEndpointRouteBuilder MapModuloPedidos(this IEndpointRouteBuilder app)
    {
        app.MapPost("/pedidos", CriarPedidoAsync).WithName("CriarPedido");
        app.MapPost("/pedidos/lote", ImportarLoteAsync)
            .Accepts<string>("text/plain")
            .Produces(StatusCodes.Status200OK, contentType: "application/x-ndjson")
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithName("ImportarLote");
        app.MapConsultasPedidos();
        app.MapPedidosVersionados();
        return app;
    }

    // Todas as dependências chegam por injeção: nenhum IServiceProvider na regra.
    private static async Task<Results<Created<PedidoResponse>, ValidationProblem, ProblemHttpResult>> CriarPedidoAsync(
        CriarPedidoRequest? request,
        ISeletorProcessador seletor,
        IRepositorioPedidos repositorio,
        LimitadorBanco limitador,
        OpcoesEscritaHttp opcoesEscrita,
        INotificadorPedidos notificador,
        CancellationToken cancellationToken)
    {
        // 1) DTO -> domínio (validação na fronteira)
        var resultado = MapeadorPedidos.ParaDominio(request);
        if (!resultado.Sucesso)
        {
            return TypedResults.ValidationProblem(resultado.Erros
                .GroupBy(e => e.Campo)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Mensagem).ToArray()));
        }

        var pedido = resultado.Valor!;

        // 2) Classificação por pattern matching; 3) processador pelo seletor do módulo
        var categoria = ClassificadorPedidos.Classificar(pedido);
        if (!seletor.Suporta(categoria))
        {
            return TypedResults.Problem(
                title: "Pedido não processável",
                detail: $"Nenhum processador disponível para a categoria {categoria}.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var processamento = await seletor.Obter(categoria).ProcessarAsync(pedido, cancellationToken);

        // 4) Persistência. Atividade 5: o SQLite aceita UM escritor por vez; passando pelo
        // mesmo LimitadorBanco da fila, escritores esperam de forma assíncrona (sem SQLITE_BUSY).
        if (opcoesEscrita.SerializarEscritas)
        {
            await limitador.ExecutarAsync(ct => repositorio.AdicionarAsync(pedido, categoria, ct), cancellationToken);
        }
        else
        {
            await repositorio.AdicionarAsync(pedido, categoria, cancellationToken);
        }

        await notificador.PublicarAsync(new PedidoAtualizado(pedido.Id.Valor, StatusPedido.Recebido, categoria, pedido.CriadoEm), cancellationToken);
        return TypedResults.Created($"/pedidos/{pedido.Id}", MapeadorResultado.ParaResposta(pedido, categoria, processamento));
    }

    // Lê o corpo de forma incremental (nunca carrega o lote inteiro) e responde em NDJSON,
    // agrupando a escrita: um flush a cada OpcoesImportacao.ItensPorFlush linhas.
    private static async Task<IResult> ImportarLoteAsync(HttpContext http, OpcoesImportacao opcoes, FilaPedidos fila, CancellationToken cancellationToken)
    {
        if (http.Request.ContentLength > opcoes.LimiteBytes)
        {
            return TypedResults.Problem(
                title: "Lote muito grande",
                detail: $"O limite é {opcoes.LimiteBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        http.Response.ContentType = "application/x-ndjson; charset=utf-8";
        await using var escritor = new EscritorNdjson(http.Response.Body, opcoes.ItensPorFlush);
        var importador = new ImportadorUtf8Pool(opcoes);

        try
        {
            await foreach (var linha in importador.LerAsync(http.Request.Body, cancellationToken))
            {
                // Com a fila em modo Wait, este await segura a leitura do corpo quando os
                // consumidores atrasam: o backpressure chega até o cliente HTTP.
                await escritor.EscreverAsync(await AvaliarAsync(linha, fila, cancellationToken), cancellationToken);
            }
        }
        catch (LoteInvalidoException ex)
        {
            await escritor.EscreverAsync(new ResultadoImportacaoResponse(0, "abortado", null, null, [ex.Message]), cancellationToken);
        }

        return TypedResults.Empty;

        static async Task<ResultadoImportacaoResponse> AvaliarAsync(ResultadoLinha linha, FilaPedidos fila, CancellationToken cancellationToken)
        {
            if (linha.Erro is not null)
            {
                return new(linha.Linha, "rejeitado", null, null, [linha.Erro]);
            }

            var resultado = MapeadorPedidos.ParaDominio(linha.Pedido);
            if (!resultado.Sucesso)
            {
                return new(linha.Linha, "rejeitado", null, null, [.. resultado.Erros.Select(e => $"{e.Campo}: {e.Mensagem}")]);
            }

            var pedido = resultado.Valor!;
            var categoria = ClassificadorPedidos.Classificar(pedido);
            return await fila.EnfileirarAsync(pedido, categoria, cancellationToken) switch
            {
                ResultadoEnfileiramento.Aceito => new(linha.Linha, "aceito", pedido.Id.ToString(), categoria.ToString(), null),
                ResultadoEnfileiramento.RejeitadoFilaCheia => new(linha.Linha, "rejeitado", pedido.Id.ToString(), categoria.ToString(), ["fila cheia"]),
                _ => new(linha.Linha, "rejeitado", pedido.Id.ToString(), categoria.ToString(), ["serviço encerrando"]),
            };
        }
    }
}

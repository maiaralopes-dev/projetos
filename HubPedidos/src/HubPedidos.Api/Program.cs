using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Application.Persistencia;
using HubPedidos.Application.Processadores;
using HubPedidos.Application.Processadores.Implementacoes;
using HubPedidos.Api;
using HubPedidos.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

// Falhar na inicialização (e não na primeira requisição) se o grafo de DI estiver errado.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateOnBuild = true;
    o.ValidateScopes = true;
});

// Catálogo construído uma única vez, inspecionando somente o assembly e o namespace
// conhecidos. Se houver duplicidade ou metadado inválido, a aplicação nem sobe.
var catalogo = CatalogoProcessadores.Construir(
    typeof(ProcessadorPadrao).Assembly,
    typeof(ProcessadorPadrao).Namespace!);

builder.Services.AddSingleton(catalogo);
foreach (var registro in catalogo.Registros)
{
    // Keyed services: a categoria é a chave; o contêiner valida o construtor no build.
    builder.Services.AddKeyedScoped(typeof(IProcessadorPedido), registro.Categoria, registro.Tipo);
}

builder.Services.AddProblemDetails();

var opcoesImportacao = builder.Configuration.GetSection("Importacao").Get<OpcoesImportacao>() ?? new OpcoesImportacao();
builder.Services.AddSingleton(opcoesImportacao);

builder.Services.AddInfraestrutura(builder.Configuration);

var app = builder.Build();

await app.Services.PrepararBancoAsync(app.Configuration);

app.MapPost("/pedidos", CriarPedidoAsync).WithName("CriarPedido");

app.MapPost("/pedidos/lote", ImportarLoteAsync)
    .Accepts<string>("text/plain")
    .Produces(StatusCodes.Status200OK, contentType: "application/x-ndjson")
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .WithName("ImportarLote");

app.MapConsultasPedidos();

app.MapGet("/processadores", (CatalogoProcessadores c) =>
    c.Registros.Select(r => new ProcessadorInfoResponse(r.Categoria.ToString(), r.Versao, r.Tipo.Name)));

app.Run();

static async Task<Results<Created<PedidoResponse>, ValidationProblem, ProblemHttpResult>> CriarPedidoAsync(
    CriarPedidoRequest? request,
    CatalogoProcessadores catalogo,
    IRepositorioPedidos repositorio,
    IServiceProvider services,
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

    // 2) Classificação por pattern matching
    var categoria = ClassificadorPedidos.Classificar(pedido);

    // 3) Seleção do processador pelo catálogo
    if (categoria is CategoriaProcessamento.Invalido || !catalogo.TentarObter(categoria, out _))
    {
        return TypedResults.Problem(
            title: "Pedido não processável",
            detail: $"Nenhum processador disponível para a categoria {categoria}.",
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    var processador = services.GetRequiredKeyedService<IProcessadorPedido>(categoria);
    var processamento = await processador.ProcessarAsync(pedido, cancellationToken);

    // 4) Persistência (atividade 3) e resposta tipada, sem expor tipos do domínio
    await repositorio.AdicionarAsync(pedido, categoria, cancellationToken);
    return TypedResults.Created($"/pedidos/{pedido.Id}", MapeadorPedidos.ParaResposta(pedido, categoria, processamento));
}

// Lê o corpo de forma incremental (nunca carrega o lote inteiro) e responde em NDJSON,
// agrupando a escrita: um flush a cada OpcoesImportacao.ItensPorFlush linhas.
static async Task<IResult> ImportarLoteAsync(HttpContext http, OpcoesImportacao opcoes, CancellationToken cancellationToken)
{
    // Corpo com tamanho declarado acima do limite: recusa antes de ler qualquer byte.
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
            await escritor.EscreverAsync(Avaliar(linha), cancellationToken);
        }
    }
    catch (LoteInvalidoException ex)
    {
        // Corpo sem Content-Length (chunked) que estourou o limite no meio: a resposta
        // já começou com 200, então o erro vira a última linha do NDJSON.
        await escritor.EscreverAsync(new ResultadoImportacaoResponse(0, "abortado", null, null, [ex.Message]), cancellationToken);
    }

    return TypedResults.Empty;

    static ResultadoImportacaoResponse Avaliar(ResultadoLinha linha)
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

        var categoria = ClassificadorPedidos.Classificar(resultado.Valor);
        return new(linha.Linha, "aceito", resultado.Valor!.Id.ToString(), categoria.ToString(), null);
    }
}

/// <summary>Exposto para WebApplicationFactory nos testes.</summary>
public partial class Program;

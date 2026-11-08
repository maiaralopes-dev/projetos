using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Application.Processadores;
using HubPedidos.Application.Processadores.Implementacoes;
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

var app = builder.Build();

app.MapPost("/pedidos", CriarPedidoAsync).WithName("CriarPedido");

app.MapGet("/processadores", (CatalogoProcessadores c) =>
    c.Registros.Select(r => new ProcessadorInfoResponse(r.Categoria.ToString(), r.Versao, r.Tipo.Name)));

app.Run();

static async Task<Results<Ok<PedidoResponse>, ValidationProblem, ProblemHttpResult>> CriarPedidoAsync(
    CriarPedidoRequest? request,
    CatalogoProcessadores catalogo,
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

    // 4) Resposta tipada, sem expor tipos do domínio
    return TypedResults.Ok(MapeadorPedidos.ParaResposta(pedido, categoria, processamento));
}

/// <summary>Exposto para WebApplicationFactory nos testes.</summary>
public partial class Program;

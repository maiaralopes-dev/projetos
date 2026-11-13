using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Contratos.V2;
using HubPedidos.Application.Persistencia;
using HubPedidos.Api.Http;
using HubPedidos.Notificacoes.TempoReal;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Modulos.Pedidos;

/// <summary>
/// Política de versão da consulta de pedido (atividade 6):
/// <list type="bullet">
/// <item>Leitura: segmento de URL <c>/api/v{n}/pedidos/{id}</c> — visível em logs, cache e documentação.</item>
/// <item>Versão ausente: a rota legada <c>/pedidos/{id}</c> continua respondendo como v1 (clientes existentes).</item>
/// <item>Versão não suportada: 400 (problem+json) com as versões aceitas.</item>
/// <item>Toda resposta traz <c>api-supported-versions</c>; a v1 traz Deprecation (RFC 9745),
/// Sunset (RFC 8594) e Link rel="successor-version".</item>
/// <item>Uso por versão é contado (métrica hub.api.requisicoes_versionadas e GET /api/versoes)
/// para decidir a remoção da v1 com dados.</item>
/// </list>
/// </summary>
public static class Versionamento
{
    public static readonly int[] Suportadas = [1, 2];
    public static readonly DateTimeOffset V1DepreciadaEm = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset V1Sunset = new(2027, 5, 31, 0, 0, 0, TimeSpan.Zero);

    public static IServiceCollection AddVersionamentoApi(this IServiceCollection services)
    {
        services.AddSingleton<UsoVersoes>();
        // Um documento OpenAPI por versão: /openapi/v1.json e /openapi/v2.json.
        services.AddOpenApi("v1", o => o.ShouldInclude = d => d.GroupName == "v1");
        services.AddOpenApi("v2", o => o.ShouldInclude = d => d.GroupName == "v2");
        return services;
    }

    public static WebApplication UseVersionamentoApi(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapGet("/api/versoes", (UsoVersoes uso) => new
        {
            suportadas = Suportadas,
            padraoSemVersao = 1,
            depreciadas = new[] { new { versao = 1, depreciadaEm = V1DepreciadaEm, sunset = V1Sunset, sucessora = 2 } },
            uso = uso.Retrato(),
        }).ExcludeFromDescription();
        return app;
    }

    public static IEndpointRouteBuilder MapPedidosVersionados(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1").WithGroupName("v1").AddEndpointFilter(FiltroVersao(1));
        v1.MapGet("/pedidos/{id:guid}", ObterV1Async).WithName("ObterPedidoV1")
            .Produces<PedidoDetalheResponse>().Produces(StatusCodes.Status304NotModified).Produces(StatusCodes.Status404NotFound);

        var v2 = app.MapGroup("/api/v2").WithGroupName("v2").AddEndpointFilter(FiltroVersao(2));
        // v2 exige identidade: cliente vê só os próprios pedidos; operador vê todos.
        // (v1 segue anônima por compatibilidade até o sunset — risco registrado no relatório.)
        v2.MapGet("/pedidos/{id:guid}", ObterV2Async).WithName("ObterPedidoV2").RequireAuthorization()
            .Produces<PedidoDetalheV2Response>().Produces(StatusCodes.Status304NotModified).Produces(StatusCodes.Status404NotFound);

        // Segmentos literais ("v1", "v2") têm precedência sobre este padrão: só versões
        // desconhecidas chegam aqui.
        app.MapGet("/api/v{versao:int}/pedidos/{id:guid}", (int versao, HttpResponse resposta) =>
        {
            resposta.Headers["api-supported-versions"] = string.Join(", ", Suportadas);
            return TypedResults.Problem(
                title: "Versão de API não suportada",
                detail: $"A versão {versao} não existe. Versões suportadas: {string.Join(", ", Suportadas)}.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["versoesSuportadas"] = Suportadas });
        }).ExcludeFromDescription();

        return app;
    }

    /// <summary>Usado também pela rota legada /pedidos/{id} (que é a v1).</summary>
    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> FiltroVersao(int versao) =>
        async (contexto, proximo) =>
        {
            var http = contexto.HttpContext;
            http.RequestServices.GetRequiredService<UsoVersoes>().Registrar(versao, http.GetEndpoint()?.DisplayName);
            http.Response.Headers["api-supported-versions"] = string.Join(", ", Suportadas);
            if (versao == 1)
            {
                http.Response.Headers["Deprecation"] = $"@{V1DepreciadaEm.ToUnixTimeSeconds()}";
                http.Response.Headers["Sunset"] = V1Sunset.ToString("R");
                http.Response.Headers.Link = "</api/v2/pedidos>; rel=\"successor-version\"";
            }

            return await proximo(contexto);
        };

    private static async Task<IResult> ObterV1Async(Guid id, IConsultasPedidos consultas, HttpContext http, CancellationToken ct) =>
        await consultas.ObterDetalheAsync(id, ct) is { } detalhe ? Etag.ResponderComEtag(http, detalhe) : TypedResults.NotFound();

    private static async Task<IResult> ObterV2Async(Guid id, IConsultasPedidos consultas, AutorizadorPedidos autorizador, HttpContext http, CancellationToken ct)
    {
        // Pedido alheio e inexistente dão a mesma resposta: não revela quais ids existem.
        if (!await autorizador.PodeVerAsync(http.User, id, ct) || await consultas.ObterDetalheAsync(id, ct) is not { } detalhe)
        {
            return TypedResults.NotFound();
        }

        return Etag.ResponderComEtag(http, AdaptadorPedidoV2.De(detalhe));
    }
}

/// <summary>Contagem de uso por versão (memória + métrica para dotnet-counters/OpenTelemetry).</summary>
public sealed class UsoVersoes
{
    private readonly ConcurrentDictionary<string, long> _contagem = new();
    private readonly Counter<long> _contador;

    public UsoVersoes(IMeterFactory meterFactory) =>
        _contador = meterFactory.Create("HubPedidos.Api").CreateCounter<long>("hub.api.requisicoes_versionadas", description: "Requisições por versão de API");

    public void Registrar(int versao, string? rota)
    {
        _contagem.AddOrUpdate($"v{versao}", 1, static (_, n) => n + 1);
        _contador.Add(1, new KeyValuePair<string, object?>("versao", versao), new KeyValuePair<string, object?>("rota", rota));
    }

    public IReadOnlyDictionary<string, long> Retrato() => _contagem.OrderBy(k => k.Key).ToDictionary();
}

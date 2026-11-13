using HubPedidos.Notificacoes;
using HubPedidos.Processamento;
using Microsoft.AspNetCore.OutputCaching;

namespace HubPedidos.Api.Http;

/// <summary>
/// Output cache SÓ em leituras públicas e iguais para todos os usuários:
/// <list type="bullet">
/// <item>Catálogo de processadores: 5 min (muda só com deploy).</item>
/// <item>Painel por região: 30 s, invalidado pela tag "painel" a cada mudança de pedido
/// feita por esta instância.</item>
/// </list>
/// Excluídos de propósito: detalhe/listagem de pedido (dados de um cliente; usam ETag),
/// métricas da fila e notificações (precisam ser atuais). A política base do ASP.NET
/// também NÃO guarda requisições com Authorization/cookie nem respostas com Set-Cookie,
/// então uma resposta privada nunca é servida a outro usuário.
/// </summary>
public static class CachePolitica
{
    public const string Catalogo = "Catalogo";
    public const string Painel = "Painel";
    public const string TagPainel = "painel";

    public static IServiceCollection AddCachePublico(this IServiceCollection services, IConfiguration configuration)
    {
        var habilitado = configuration.GetValue("Cache:Habilitado", true);
        services.AddOutputCache(o =>
        {
            o.AddPolicy(Catalogo, b => b.Expire(TimeSpan.FromMinutes(5)).Tag("catalogo").With(_ => habilitado));
            o.AddPolicy(Painel, b => b.Expire(TimeSpan.FromSeconds(30)).Tag(TagPainel).SetVaryByQuery([]).With(_ => habilitado));
        });
        services.AddSingleton<ICanalNotificacao, CanalInvalidacaoCache>();
        return services;
    }
}

/// <summary>Canal de notificação implementado pela API: pedido mudou → painel em cache é descartado.</summary>
public sealed class CanalInvalidacaoCache(IOutputCacheStore store) : ICanalNotificacao
{
    public ValueTask PublicarAsync(PedidoAtualizado evento, CancellationToken cancellationToken) =>
        store.EvictByTagAsync(CachePolitica.TagPainel, cancellationToken);
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HubPedidos.Tests.Versionamento;

/// <summary>
/// Representa o FRONT-END que consome a v1. Tem os PRÓPRIOS tipos, copiados do contrato
/// publicado em 2026-11 — de propósito, não reutiliza os DTOs do servidor: se o servidor
/// mudar a v1, este cliente quebra, como quebraria um cliente real.
/// A desserialização é ESTRITA (membro desconhecido = erro) para detectar qualquer
/// alteração no contrato v1, inclusive campos novos.
/// </summary>
public sealed class ClientePainelV1(HttpClient http)
{
    public static readonly JsonSerializerOptions Estrito = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public sealed record ItemV1(string Sku, int Quantidade, decimal PrecoUnitario, decimal Subtotal);

    public sealed record EnderecoV1(string? Logradouro, string? Cidade, string? Uf, string? Cep);

    public sealed record MetadadosV1(string? Canal, string? Origem, string? Campanha);

    public sealed record PedidoV1(
        Guid Id, string ClienteId, string Regiao, string Prioridade, string Status, string Categoria,
        decimal Total, DateTime CriadoEmUtc, EnderecoV1? Entrega, MetadadosV1 Metadados, IReadOnlyList<ItemV1> Itens);

    public async Task<PedidoV1> ObterAsync(Guid id, string rota = "/api/v1/pedidos")
    {
        using var resposta = await http.GetAsync($"{rota}/{id}");
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PedidoV1>(Estrito))!;
    }

    /// <summary>Regra de tela do painel v1: o total exibido é o valor numérico da v1.</summary>
    public static string TotalFormatado(PedidoV1 p) => p.Total.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}

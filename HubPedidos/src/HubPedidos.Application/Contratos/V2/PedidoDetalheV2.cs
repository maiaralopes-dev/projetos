using System.Globalization;

namespace HubPedidos.Application.Contratos.V2;

// Contrato v2 do detalhe do pedido. Mudanças INCOMPATÍVEIS em relação à v1 (deliberadas):
//  1. Valores monetários viram objeto { valor: "8.00", moeda: "BRL" } com valor em STRING:
//     number em JSON vira double em JavaScript e perde precisão em valores grandes.
//  2. "criadoEmUtc" (sem fuso, ex.: "2026-11-09T01:20:00") vira "criadoEm" com fuso explícito
//     (ex.: "2026-11-09T01:20:00+00:00"): sem fuso, cada cliente interpretava a hora de um jeito.
// O restante permanece igual (mesmos nomes e significados).

public sealed record DinheiroV2(string Valor, string Moeda)
{
    public static DinheiroV2 Brl(decimal valor) => new(valor.ToString("F2", CultureInfo.InvariantCulture), "BRL");
}

public sealed record ItemPedidoV2(string Sku, int Quantidade, DinheiroV2 PrecoUnitario, DinheiroV2 Subtotal);

public sealed record PedidoDetalheV2Response(
    Guid Id,
    string ClienteId,
    string Regiao,
    string Prioridade,
    string Status,
    string Categoria,
    DinheiroV2 Total,
    DateTimeOffset CriadoEm,
    EnderecoDto? Entrega,
    MetadadosDto Metadados,
    IReadOnlyList<ItemPedidoV2> Itens);

/// <summary>
/// Tradução v1 → v2 a partir do MESMO resultado de consulta (sem consulta extra). Mantém
/// as duas representações isoladas: a v1 nunca é alterada para servir a v2.
/// </summary>
public static class AdaptadorPedidoV2
{
    public static PedidoDetalheV2Response De(PedidoDetalheResponse v1) => new(
        v1.Id,
        v1.ClienteId,
        v1.Regiao,
        v1.Prioridade,
        v1.Status,
        v1.Categoria,
        DinheiroV2.Brl(v1.Total),
        new DateTimeOffset(DateTime.SpecifyKind(v1.CriadoEmUtc, DateTimeKind.Utc)),
        v1.Entrega,
        v1.Metadados,
        [.. v1.Itens.Select(i => new ItemPedidoV2(i.Sku, i.Quantidade, DinheiroV2.Brl(i.PrecoUnitario), DinheiroV2.Brl(i.Subtotal)))]);
}

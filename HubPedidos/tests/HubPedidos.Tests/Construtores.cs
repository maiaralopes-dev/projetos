using HubPedidos.Application.Contratos;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests;

internal static class Construtores
{
    public static ItemPedido Item(string sku = "SKU-1", int qtd = 1, decimal preco = 10m) => new(sku, qtd, preco);

    public static Pedido Pedido(Regiao regiao = Regiao.Sudeste, Prioridade prioridade = Prioridade.Normal, params ItemPedido[] itens) =>
        Domain.Pedidos.Pedido.Criar("CLI-1", regiao, prioridade, itens.Length > 0 ? itens : [Item()]);

    public static CriarPedidoRequest RequestValido(List<ItemPedidoRequest>? itens = null) =>
        new("CLI-1", "Sudeste", "Normal", itens ?? [new("caneta", 2, 3.50m)]);
}

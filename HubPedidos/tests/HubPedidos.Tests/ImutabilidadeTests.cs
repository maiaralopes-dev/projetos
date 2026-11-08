using HubPedidos.Domain.Pedidos;
using static HubPedidos.Tests.Construtores;

namespace HubPedidos.Tests;

public class ImutabilidadeTests
{
    [Fact]
    public void ItemPedido_tem_igualdade_por_valor()
    {
        Assert.Equal(new ItemPedido("abc", 2, 9.90m), new ItemPedido("ABC ", 2, 9.90m)); // SKU normalizado
        Assert.NotEqual(new ItemPedido("abc", 2, 9.90m), new ItemPedido("abc", 3, 9.90m));
    }

    [Fact]
    public void Copia_com_with_nao_altera_o_original()
    {
        var original = Item("SKU-1", 1, 10m);

        var copia = original with { Quantidade = 5 };

        Assert.Equal(1, original.Quantidade);
        Assert.Equal(5, copia.Quantidade);
        Assert.NotSame(original, copia);
    }

    [Fact]
    public void With_tambem_passa_pela_validacao_do_init()
    {
        var item = Item();

        Assert.Throws<DomainException>(() => item with { Quantidade = 0 });
        Assert.Throws<DomainException>(() => item with { PrecoUnitario = 1.999m });
    }

    [Fact]
    public void Pedido_usa_igualdade_por_identidade()
    {
        var pedido = Pedido();
        var repriorizado = pedido with { Prioridade = Prioridade.Urgente };

        Assert.Equal(Prioridade.Normal, pedido.Prioridade);       // original intacto
        Assert.Equal(pedido, repriorizado);                       // mesma entidade (mesmo Id)
        Assert.NotEqual(pedido, Pedido());                        // mesmos dados, outro Id
    }

    [Fact]
    public void With_no_pedido_nao_permite_estado_invalido()
    {
        var pedido = Pedido();

        Assert.Throws<DomainException>(() => pedido with { Itens = [] });
        Assert.Throws<DomainException>(() => pedido with { Regiao = (Regiao)99 });
        Assert.Throws<DomainException>(() => pedido with { Id = default });
    }

    [Fact]
    public void Pedido_nao_compartilha_a_colecao_de_entrada()
    {
        var itens = new List<ItemPedido> { Item("A") };
        var pedido = Pedido(itens: [.. itens]);

        var criadoComLista = Domain.Pedidos.Pedido.Criar("CLI", Regiao.Sul, Prioridade.Normal, itens);
        itens.Add(Item("B"));

        Assert.Single(criadoComLista.Itens);
        Assert.Single(pedido.Itens);
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("SKU", 0, 1)]
    [InlineData("SKU", 1001, 1)]
    [InlineData("SKU", 1, 0)]
    [InlineData("SKU", 1, -5)]
    public void ItemPedido_rejeita_invariantes_quebradas(string sku, int qtd, decimal preco) =>
        Assert.Throws<DomainException>(() => new ItemPedido(sku, qtd, preco));

    [Fact]
    public void Pedido_rejeita_sku_repetido() =>
        Assert.Throws<DomainException>(() => Pedido(itens: [Item("A"), Item("a")]));
}

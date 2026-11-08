using HubPedidos.Application.Contratos;
using HubPedidos.Application.Mapeamento;
using static HubPedidos.Tests.Construtores;

namespace HubPedidos.Tests;

public class MapeadorTests
{
    [Fact]
    public void Dto_valido_vira_pedido()
    {
        var resultado = MapeadorPedidos.ParaDominio(RequestValido());

        Assert.True(resultado.Sucesso);
        Assert.Equal(7.00m, resultado.Valor!.Total);
        Assert.Equal("CANETA", resultado.Valor.Itens[0].Sku);
    }

    [Fact]
    public void Dto_invalido_e_rejeitado_antes_do_dominio_com_todos_os_erros()
    {
        var request = new CriarPedidoRequest("", "Marte", "99",
        [
            new("", 0, 1.234m),
            new("X", 1, 1m),
            new("x", 1, 1m),
        ]);

        // Se o domínio fosse chamado, uma DomainException seria lançada; aqui não há exceção.
        var resultado = MapeadorPedidos.ParaDominio(request);

        Assert.False(resultado.Sucesso);
        Assert.Null(resultado.Valor);
        Assert.Equal(
            ["clienteId", "itens[0].precoUnitario", "itens[0].quantidade", "itens[0].sku", "itens[2].sku", "prioridade", "regiao"],
            resultado.Erros.Select(e => e.Campo).Order());
    }

    [Theory]
    [InlineData("2")]   // Enum.TryParse aceitaria (numérico)
    [InlineData("-1")]
    [InlineData("Sul,Norte")] // Enum.TryParse aceitaria (flags)
    [InlineData(" ")]
    [InlineData(null)]
    public void Regiao_aceita_somente_nomes_definidos(string? regiao)
    {
        var resultado = MapeadorPedidos.ParaDominio(RequestValido() with { Regiao = regiao });

        Assert.Contains(resultado.Erros, e => e.Campo == "regiao");
    }

    [Fact]
    public void Nomes_de_enum_sao_case_insensitive() =>
        Assert.True(MapeadorPedidos.ParaDominio(RequestValido() with { Regiao = "sudeste", Prioridade = "URGENTE" }).Sucesso);

    [Fact]
    public void Lista_do_dto_alterada_depois_nao_afeta_o_pedido()
    {
        var itens = new List<ItemPedidoRequest> { new("A", 1, 1m) };
        var pedido = MapeadorPedidos.ParaDominio(RequestValido(itens)).Valor!;

        itens.Add(new("B", 1, 1m));
        itens[0] = new("Z", 99, 99m);

        Assert.Single(pedido.Itens);
        Assert.Equal("A", pedido.Itens[0].Sku);
    }

    [Fact]
    public void Requisicao_nula_e_sem_itens_sao_rejeitadas()
    {
        Assert.False(MapeadorPedidos.ParaDominio(null).Sucesso);
        Assert.Contains(MapeadorPedidos.ParaDominio(RequestValido([])).Erros, e => e.Campo == "itens");
        Assert.Contains(MapeadorPedidos.ParaDominio(RequestValido() with { Itens = null }).Erros, e => e.Campo == "itens");
    }
}

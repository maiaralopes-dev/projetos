using System.Globalization;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests;

public class PedidoIdTests
{
    [Fact]
    public void Ida_e_volta_por_texto_preserva_o_valor()
    {
        var id = PedidoId.Novo();

        Assert.Equal(id, PedidoId.Parse(id.ToString(), CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryParse_rejeita_texto_invalido_sem_excecao(string? texto)
    {
        Assert.False(PedidoId.TryParse(texto, null, out var id));
        Assert.False(id.EhValido);
    }

    [Fact]
    public void Funciona_via_restricao_generica_IParsable() =>
        Assert.True(Parse<PedidoId>(Guid.NewGuid().ToString()).EhValido);

    [Fact]
    public void Default_nao_e_valido() => Assert.False(default(PedidoId).EhValido);

    private static T Parse<T>(string s) where T : IParsable<T> => T.Parse(s, CultureInfo.InvariantCulture);
}

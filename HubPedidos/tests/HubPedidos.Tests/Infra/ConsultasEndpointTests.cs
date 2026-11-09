using System.Net;
using System.Net.Http.Json;
using HubPedidos.Application.Contratos;

namespace HubPedidos.Tests.Infra;

public class ConsultasEndpointTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> CriarAsync(string cliente = "CLI-E2E", EnderecoDto? entrega = null)
    {
        var resposta = await _client.PostAsJsonAsync("/pedidos",
            new CriarPedidoRequest(cliente, "Sul", "Normal", [new("caneta", 2, 3.50m), new("lápis", 1, 1m)], entrega, new MetadadosDto("web", null, "lancamento")));
        resposta.EnsureSuccessStatusCode();
        return Guid.Parse((await resposta.Content.ReadFromJsonAsync<PedidoResponse>())!.Id);
    }

    [Fact]
    public async Task Pedido_criado_e_persistido_e_consultado_pelo_detalhe()
    {
        var id = await CriarAsync(entrega: new("Av. Brasil, 1", "Recife", "PE", "50000000"));

        var detalhe = await _client.GetFromJsonAsync<PedidoDetalheResponse>($"/pedidos/{id}");

        Assert.Equal(id, detalhe!.Id);
        Assert.Equal(8.00m, detalhe.Total);
        Assert.Equal(["CANETA", "LÁPIS"], detalhe.Itens.Select(i => i.Sku));
        Assert.Equal("Recife", detalhe.Entrega!.Cidade);
        Assert.Equal(new MetadadosDto("web", null, "lancamento"), detalhe.Metadados);
    }

    [Fact]
    public async Task Status_segue_a_maquina_de_estados()
    {
        var id = await CriarAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync($"/pedidos/{id}/status", new AlterarStatusRequest("EmProcessamento"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"/pedidos/{id}/status", new AlterarStatusRequest("Recebido"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/pedidos/{id}/status", new AlterarStatusRequest("Voando"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/pedidos/{Guid.NewGuid()}/status", new AlterarStatusRequest("Cancelado"))).StatusCode);

        var detalhe = await _client.GetFromJsonAsync<PedidoDetalheResponse>($"/pedidos/{id}");
        Assert.Equal("EmProcessamento", detalhe!.Status);
    }

    [Fact]
    public async Task Listagem_filtra_por_cliente_e_valida_parametros()
    {
        var cliente = $"CLI-{Guid.NewGuid():N}"[..20];
        await CriarAsync(cliente);
        await CriarAsync(cliente);

        var pagina = await _client.GetFromJsonAsync<PaginaResponse<PedidoResumoResponse>>($"/pedidos?clienteId={cliente}&tamanho=500");

        Assert.Equal(2, pagina!.Total);
        Assert.Equal(100, pagina.Tamanho); // tamanho limitado no servidor
        Assert.All(pagina.Itens, i => Assert.Equal((cliente, 2), (i.ClienteId, i.QuantidadeItens)));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/pedidos?regiao=Marte")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/pedidos/{Guid.NewGuid()}")).StatusCode);
    }
}

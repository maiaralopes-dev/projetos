using System.Net;
using System.Net.Http.Json;
using HubPedidos.Application.Contratos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests;

public class EndpointTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("Sudeste", "Normal", 2, 3.50, "Padrao", "ProcessadorPadrao")]
    [InlineData("Sul", "Urgente", 1, 50, "Expresso", "ProcessadorExpresso")]
    [InlineData("Norte", "Normal", 1, 6000, "AltoValor", "ProcessadorAntifraude")]
    [InlineData("Internacional", "Alta", 1, 200, "Internacional", "ProcessadorInternacional")]
    [InlineData("Internacional", "Normal", 2, 5000, "RevisaoManual", "ProcessadorAntifraude")]
    public async Task Fluxo_completo_classifica_e_seleciona_processador(string regiao, string prioridade, int qtd, decimal preco, string categoria, string processador)
    {
        var request = new CriarPedidoRequest("CLI-9", regiao, prioridade, [new("sku-1", qtd, preco)]);

        var resposta = await _client.PostAsJsonAsync("/pedidos", request);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<PedidoResponse>();
        Assert.Equal($"/pedidos/{corpo!.Id}", resposta.Headers.Location!.OriginalString);
        Assert.Equal(categoria, corpo.Categoria);
        Assert.Equal(processador, corpo.Processador);
        Assert.Equal(qtd * preco, corpo.Total);
        Assert.True(Guid.TryParse(corpo.Id, out _));
    }

    [Fact]
    public async Task Dto_invalido_retorna_400_com_erros_por_campo()
    {
        var request = new CriarPedidoRequest("", "Marte", "Normal", []);

        var resposta = await _client.PostAsJsonAsync("/pedidos", request);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var problema = await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(["clienteId", "itens", "regiao"], problema!.Errors.Keys.Order());
    }

    [Fact]
    public async Task Catalogo_e_exposto_em_ordem_deterministica()
    {
        var itens = await _client.GetFromJsonAsync<ProcessadorInfoResponse[]>("/processadores");

        Assert.Equal(["Padrao", "Atacado", "Expresso", "AltoValor", "Internacional", "RevisaoManual"], itens!.Select(i => i.Categoria));
    }
}

public class EscritaSerializadaTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public async Task Posts_concorrentes_passam_pelo_limitador_e_nunca_escrevem_em_paralelo()
    {
        var client = factory.CreateClient();
        var limitador = factory.Services.GetRequiredService<HubPedidos.Processamento.Fila.LimitadorBanco>();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            client.PostAsJsonAsync("/pedidos", new CriarPedidoRequest($"CLI-{i}", "Sul", "Normal", [new("x", 1, 1m)]))));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(1, limitador.MaximoObservado); // SQLite: um escritor por vez
        Assert.Equal(1, limitador.Disponiveis);
    }
}

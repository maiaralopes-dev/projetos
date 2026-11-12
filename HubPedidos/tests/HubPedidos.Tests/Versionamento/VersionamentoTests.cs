using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Contratos.V2;

namespace HubPedidos.Tests.Versionamento;

public class VersionamentoTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient();

    private async Task<Guid> CriarAsync()
    {
        var r = await _http.PostAsJsonAsync("/pedidos", new CriarPedidoRequest("CLI-V", "Sul", "Normal",
            [new("caneta", 3, 999_999.99m), new("lapis", 1, 0.10m)], null, new MetadadosDto("web", null, null)));
        r.EnsureSuccessStatusCode();
        return Guid.Parse((await r.Content.ReadFromJsonAsync<PedidoResponse>())!.Id);
    }

    [Theory]
    [InlineData("/api/v1/pedidos")]
    [InlineData("/pedidos")] // rota legada sem versão = v1
    public async Task Cliente_v1_continua_funcionando_na_aplicacao_que_tem_v2(string rota)
    {
        var id = await CriarAsync();
        var cliente = new ClientePainelV1(_http);

        var pedido = await cliente.ObterAsync(id, rota); // falharia com qualquer campo novo/renomeado

        // Semântica preservada, não só o formato:
        Assert.Equal(pedido.Itens.Sum(i => i.Subtotal), pedido.Total);
        Assert.All(pedido.Itens, i => Assert.Equal(i.Quantidade * i.PrecoUnitario, i.Subtotal));
        Assert.Equal(3_000_000.07m, pedido.Total);                 // decimal exato, sem perda
        Assert.Equal("3.000.000,07", ClientePainelV1.TotalFormatado(pedido));
        Assert.InRange(pedido.CriadoEmUtc, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1)); // é UTC
        Assert.Contains(pedido.Status, new[] { "Recebido", "EmProcessamento", "Concluido", "Cancelado" });
    }

    [Fact]
    public async Task Contrato_v1_e_identico_ao_publicado()
    {
        var id = await CriarAsync();
        using var json = JsonDocument.Parse(await _http.GetStringAsync($"/api/v1/pedidos/{id}"));

        // "Snapshot" do contrato v1: nomes e tipos JSON de cada campo de topo.
        Assert.Equal(
            "categoria:String clienteId:String criadoEmUtc:String entrega:Null id:String itens:Array metadados:Object prioridade:String regiao:String status:String total:Number",
            string.Join(' ', json.RootElement.EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}").Order()));
        Assert.Equal("precoUnitario:Number quantidade:Number sku:String subtotal:Number",
            string.Join(' ', json.RootElement.GetProperty("itens")[0].EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}").Order()));
    }

    [Fact]
    public async Task V2_tem_a_mudanca_incompativel_documentada_e_quebraria_o_cliente_v1()
    {
        var id = await CriarAsync();

        var v2 = await _http.GetFromJsonAsync<PedidoDetalheV2Response>($"/api/v2/pedidos/{id}");
        Assert.Equal(new DinheiroV2("3000000.07", "BRL"), v2!.Total);
        Assert.Equal(TimeSpan.Zero, v2.CriadoEm.Offset);
        Assert.Contains("+00:00", await _http.GetStringAsync($"/api/v2/pedidos/{id}"));

        // O cliente v1 NÃO entende a v2: é por isso que a v1 continua existindo.
        await Assert.ThrowsAsync<JsonException>(() => new ClientePainelV1(_http).ObterAsync(id, "/api/v2/pedidos"));
    }

    [Fact]
    public async Task V1_anuncia_depreciacao_e_sucessora_e_v2_nao()
    {
        var id = await CriarAsync();

        var v1 = await _http.GetAsync($"/api/v1/pedidos/{id}");
        var v2 = await _http.GetAsync($"/api/v2/pedidos/{id}");

        Assert.Equal("1, 2", v1.Headers.GetValues("api-supported-versions").Single());
        Assert.Equal("@1794441600", v1.Headers.GetValues("Deprecation").Single());
        Assert.Equal("Mon, 31 May 2027 00:00:00 GMT", v1.Headers.GetValues("Sunset").Single());
        Assert.Contains("rel=\"successor-version\"", v1.Headers.GetValues("Link").Single());
        Assert.False(v2.Headers.Contains("Deprecation"));
        Assert.Equal("1, 2", v2.Headers.GetValues("api-supported-versions").Single());
    }

    [Fact]
    public async Task Versao_nao_suportada_segue_a_politica_explicita()
    {
        var resposta = await _http.GetAsync($"/api/v3/pedidos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType!.MediaType);
        Assert.Equal("1, 2", resposta.Headers.GetValues("api-supported-versions").Single());
        using var problema = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal([1, 2], problema.RootElement.GetProperty("versoesSuportadas").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task Documentacao_openapi_e_separada_por_versao()
    {
        var v1 = await _http.GetStringAsync("/openapi/v1.json");
        var v2 = await _http.GetStringAsync("/openapi/v2.json");

        Assert.Contains("/api/v1/pedidos/{id}", v1);
        Assert.DoesNotContain("/api/v2/", v1);
        Assert.Contains("/api/v2/pedidos/{id}", v2);
        Assert.DoesNotContain("/api/v1/", v2);
        Assert.Contains("DinheiroV2", v2);
    }

    [Fact]
    public async Task Uso_por_versao_e_observavel()
    {
        var id = await CriarAsync();
        var antes = await _http.GetFromJsonAsync<JsonElement>("/api/versoes");

        await _http.GetAsync($"/api/v1/pedidos/{id}");
        await _http.GetAsync($"/pedidos/{id}");
        await _http.GetAsync($"/api/v2/pedidos/{id}");
        var depois = await _http.GetFromJsonAsync<JsonElement>("/api/versoes");

        long Uso(JsonElement e, string v) => e.GetProperty("uso").TryGetProperty(v, out var n) ? n.GetInt64() : 0;
        Assert.Equal(2, Uso(depois, "v1") - Uso(antes, "v1"));
        Assert.Equal(1, Uso(depois, "v2") - Uso(antes, "v2"));
        Assert.Equal("2027-05-31T00:00:00+00:00", depois.GetProperty("depreciadas")[0].GetProperty("sunset").GetString());
    }
}

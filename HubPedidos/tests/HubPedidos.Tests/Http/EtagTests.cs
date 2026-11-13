using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HubPedidos.Application.Contratos;

namespace HubPedidos.Tests.Http;

public class EtagTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _anonimo = factory.CreateClient();

    private async Task<Guid> CriarAsync(string cliente)
    {
        var r = await _anonimo.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(cliente, "Sul", "Normal", [new("x", 2, 5m)]));
        r.EnsureSuccessStatusCode();
        return Guid.Parse((await r.Content.ReadFromJsonAsync<PedidoResponse>())!.Id);
    }

    private static HttpRequestMessage Get(string url, string? ifNoneMatch = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (ifNoneMatch is not null)
        {
            req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return req;
    }

    [Theory]
    [InlineData("/api/v1/pedidos")]
    [InlineData("/api/v2/pedidos")]
    public async Task Etag_e_estavel_quando_nada_muda_e_304_vem_sem_corpo(string rota)
    {
        var id = await CriarAsync("CLI-E1");
        var http = factory.ClienteAutenticado("CLI-E1");

        var primeira = await http.SendAsync(Get($"{rota}/{id}"));
        var segunda = await http.SendAsync(Get($"{rota}/{id}"));
        var etag = primeira.Headers.ETag!;

        Assert.False(etag.IsWeak);
        Assert.Equal(etag, segunda.Headers.ETag);
        Assert.True(primeira.Headers.CacheControl is { Private: true, NoCache: true });

        var condicional = await http.SendAsync(Get($"{rota}/{id}", etag.ToString()));
        Assert.Equal(HttpStatusCode.NotModified, condicional.StatusCode);
        Assert.Empty(await condicional.Content.ReadAsByteArrayAsync());
        Assert.Equal(etag, condicional.Headers.ETag);
    }

    [Fact]
    public async Task Etag_muda_quando_a_representacao_muda_e_valor_obsoleto_recebe_a_atual()
    {
        var id = await CriarAsync("CLI-E2");
        var http = factory.ClienteAutenticado("CLI-E2");
        var antiga = (await http.SendAsync(Get($"/api/v2/pedidos/{id}"))).Headers.ETag!.ToString();

        await http.PutAsJsonAsync($"/pedidos/{id}/status", new AlterarStatusRequest("Cancelado"));
        var resposta = await http.SendAsync(Get($"/api/v2/pedidos/{id}", antiga));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.NotEqual(antiga, resposta.Headers.ETag!.ToString());
        Assert.Contains("\"status\":\"Cancelado\"", await resposta.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("lixo-sem-aspas")]
    [InlineData("\"nao-existe\"")]
    [InlineData("W/\"tambem-nao\"")]
    public async Task If_none_match_invalido_ou_desconhecido_devolve_a_representacao(string valor)
    {
        var id = await CriarAsync("CLI-E3");

        var resposta = await _anonimo.SendAsync(Get($"/api/v1/pedidos/{id}", valor));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.NotEmpty(await resposta.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Comparacao_fraca_e_asterisco_e_lista_dao_304()
    {
        var id = await CriarAsync("CLI-E4");
        var etag = (await _anonimo.SendAsync(Get($"/api/v1/pedidos/{id}"))).Headers.ETag!.Tag;

        foreach (var valor in new[] { $"W/{etag}", "*", $"\"outra\", {etag}" })
        {
            Assert.Equal(HttpStatusCode.NotModified, (await _anonimo.SendAsync(Get($"/api/v1/pedidos/{id}", valor))).StatusCode);
        }
    }

    [Fact]
    public async Task Cada_versao_tem_a_propria_etag_e_a_rota_legada_e_igual_a_v1()
    {
        var id = await CriarAsync("CLI-E5");
        var http = factory.ClienteAutenticado("CLI-E5");

        var v1 = (await http.SendAsync(Get($"/api/v1/pedidos/{id}"))).Headers.ETag;
        var legada = (await http.SendAsync(Get($"/pedidos/{id}"))).Headers.ETag;
        var v2 = (await http.SendAsync(Get($"/api/v2/pedidos/{id}"))).Headers.ETag;

        Assert.Equal(v1, legada);
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public async Task If_match_atual_permite_e_obsoleto_da_412_sem_gravar()
    {
        var id = await CriarAsync("CLI-E6");
        var http = factory.ClienteAutenticado("CLI-E6");
        var etag = (await http.SendAsync(Get($"/api/v2/pedidos/{id}"))).Headers.ETag!;

        using var ok = new HttpRequestMessage(HttpMethod.Put, $"/pedidos/{id}/status") { Content = JsonContent.Create(new AlterarStatusRequest("EmProcessamento")) };
        ok.Headers.IfMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NoContent, (await http.SendAsync(ok)).StatusCode);

        // Outro usuário (ou aba) ainda com a ETag antiga tenta cancelar:
        using var obsoleto = new HttpRequestMessage(HttpMethod.Put, $"/pedidos/{id}/status") { Content = JsonContent.Create(new AlterarStatusRequest("Cancelado")) };
        obsoleto.Headers.IfMatch.Add(etag);
        var resposta = await http.SendAsync(obsoleto);

        Assert.Equal(HttpStatusCode.PreconditionFailed, resposta.StatusCode);
        Assert.Contains("\"status\":\"EmProcessamento\"", await http.GetStringAsync($"/api/v2/pedidos/{id}")); // nada gravado

        // Sem If-Match: comportamento antigo preservado (compatibilidade).
        Assert.Equal(HttpStatusCode.NoContent, (await http.PutAsJsonAsync($"/pedidos/{id}/status", new AlterarStatusRequest("Concluido"))).StatusCode);
    }

    [Fact]
    public async Task V2_exige_identidade_e_esconde_pedido_alheio()
    {
        var id = await CriarAsync("CLI-E7");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonimo.GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.ClienteAutenticado("CLI-OUTRO").GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.ClienteAutenticado("CLI-E7").GetAsync($"/api/v2/pedidos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ClienteAutenticado("CLI-E7").GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ClienteAutenticado(null, operador: true).GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
    }
}

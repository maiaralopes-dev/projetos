using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PortalTarefas.Tests;

/// <summary>Testes de integração do pipeline e dos endpoints da atividade 1.</summary>
public class Atividade1PipelineTests(PortalWebFactory factory) : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b => b.UseEnvironment("Development"))
        .CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task Rota_valida_retorna_200_e_correlation_id()
    {
        var resp = await _client.GetAsync("/api/v1/tarefas/1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.Contains("X-Correlation-Id"));
    }

    [Theory]
    [InlineData("/api/v1/tarefas/999")]
    [InlineData("/api/v1/tarefas/abc")]
    public async Task Rota_inexistente_ou_invalida_retorna_404_problem(string url)
    {
        var resp = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Filtro_query_e_cabecalho_opcional()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tarefas?situacao=Pendente");
        req.Headers.Add("X-Cliente-Id", "teste");
        var resp = await _client.SendAsync(req);
        Assert.Equal("teste", resp.Headers.GetValues("X-Cliente-Atendido").Single());
        var itens = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(itens.EnumerateArray(), t => Assert.Equal("Pendente", t.GetProperty("situacao").GetString()));
    }

    [Fact]
    public async Task Post_valido_retorna_201_com_location()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/tarefas", new { titulo = "Nova tarefa", situacao = "Pendente" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
    }

    [Fact]
    public async Task Post_invalido_retorna_400_com_erros()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/tarefas", new { titulo = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("Titulo", out _));
    }

    [Fact]
    public async Task Excecao_vira_500_sem_detalhes_sensiveis()
    {
        var resp = await _client.GetAsync("/api/minimal/diagnostico/erro");
        var texto = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.DoesNotContain("db-interno", texto);
        Assert.DoesNotContain("InvalidOperationException", texto);
        Assert.Contains("correlationId", texto);
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://evil.example", false)]
    public async Task Cors_somente_para_origem_autorizada(string origem, bool permitido)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/tarefas");
        req.Headers.Add("Origin", origem);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        var resp = await _client.SendAsync(req);
        Assert.Equal(permitido, resp.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Minimal_e_mvc_retornam_contrato_equivalente()
    {
        var a = await _client.GetFromJsonAsync<JsonElement>("/api/minimal/diagnostico");
        var b = await _client.GetFromJsonAsync<JsonElement>("/api/mvc/diagnostico");
        var propsA = a.EnumerateObject().Select(p => p.Name).Order();
        var propsB = b.EnumerateObject().Select(p => p.Name).Order();
        Assert.Equal(propsA, propsB);
    }
}

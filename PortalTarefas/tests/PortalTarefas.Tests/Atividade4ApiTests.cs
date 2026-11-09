using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortalTarefas.Tests;

/// <summary>Testes da API REST v2: paginação, ordenação, filtros, Problem Details e contrato OpenAPI.</summary>
public class Atividade4ApiTests(PortalWebFactory factory) : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client = factory.CriarCliente("carla");

    private async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Theory]
    [InlineData("pagina=0")]
    [InlineData("tamanho=0")]
    [InlineData("tamanho=51")]
    public async Task Paginacao_fora_dos_limites_retorna_400(string query)
    {
        var resp = await _client.GetAsync($"/api/v2/tarefas?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public async Task Paginacao_nos_limites_e_aceita(int tamanho)
    {
        var corpo = await Json(await _client.GetAsync($"/api/v2/tarefas?tamanho={tamanho}"));
        var total = corpo.GetProperty("totalItens").GetInt32();
        Assert.Equal(Math.Min(tamanho, total), corpo.GetProperty("itens").GetArrayLength());
        Assert.Equal(tamanho, corpo.GetProperty("tamanho").GetInt32());
    }

    [Fact]
    public async Task Pagina_alem_do_fim_retorna_lista_vazia()
    {
        var corpo = await Json(await _client.GetAsync("/api/v2/tarefas?pagina=999"));
        Assert.Equal(0, corpo.GetProperty("itens").GetArrayLength());
    }

    [Fact]
    public async Task Ordenacao_por_campo_permitido()
    {
        var corpo = await Json(await _client.GetAsync("/api/v2/tarefas?ordenarPor=titulo&direcao=desc&tamanho=50"));
        var titulos = corpo.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("titulo").GetString()!).ToList();
        Assert.Equal(titulos.OrderByDescending(t => t, StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), true)), titulos);
    }

    [Fact]
    public async Task Ordenacao_por_campo_nao_permitido_retorna_400()
    {
        var resp = await _client.GetAsync("/api/v2/tarefas?ordenarPor=versao");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True((await Json(resp)).GetProperty("errors").TryGetProperty("ordenarPor", out _));
    }

    [Fact]
    public async Task Combinacao_de_dois_filtros()
    {
        var corpo = await Json(await _client.GetAsync("/api/v2/tarefas?situacao=Pendente&prioridade=Alta"));
        Assert.All(corpo.GetProperty("itens").EnumerateArray(), i =>
        {
            Assert.Equal("Pendente", i.GetProperty("situacao").GetString());
            Assert.Equal("Alta", i.GetProperty("prioridade").GetString());
        });
    }

    [Fact]
    public async Task Crud_completo_com_location_404_e_409()
    {
        var criar = await _client.PostAsJsonAsync("/api/v2/tarefas", new { titulo = "Tarefa do teste de API", prioridade = "Baixa" });
        Assert.Equal(HttpStatusCode.Created, criar.StatusCode);
        var criada = await Json(criar);
        var url = criar.Headers.Location!.ToString();
        var versao = criada.GetProperty("versao").GetString();

        var duplicada = await _client.PostAsJsonAsync("/api/v2/tarefas", new { titulo = "Tarefa do teste de API" });
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);

        var put = new { titulo = "Tarefa do teste de API", situacao = "EmAndamento", prioridade = "Baixa", versao };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, put)).StatusCode);
        var conflito = await _client.PutAsJsonAsync(url, put);   // mesma versão antiga
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        Assert.True((await Json(conflito)).TryGetProperty("versaoAtual", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(url)).StatusCode);
        var ausente = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal("application/problem+json", ausente.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_invalido_retorna_problem_details_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/v2/tarefas", new { titulo = "ab", prioridade = "Alta" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(400, (await Json(resp)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Cabecalhos_de_paginacao_sao_enviados()
    {
        var resp = await _client.GetAsync("/api/v2/tarefas?tamanho=3&pagina=2");
        Assert.Equal("9", resp.Headers.GetValues("X-Total-Count").Single());
        var link = resp.Headers.GetValues("Link").Single();
        Assert.Contains("rel=\"prev\"", link);
        Assert.Contains("rel=\"next\"", link);
    }

    [Fact]
    public async Task V1_sinaliza_descontinuacao()
    {
        var resp = await _client.GetAsync("/api/v1/tarefas");
        Assert.True(resp.Headers.Contains("Deprecation"));
        Assert.Contains("successor-version", resp.Headers.GetValues("Link").Single());
    }

    /// <summary>
    /// Teste de contrato (snapshot): o documento servido pela aplicação deve ser idêntico ao artefato
    /// versionado em docs/openapi. Mudou o contrato? Rode o build, revise o diff e regenere o cliente TS.
    /// </summary>
    [Fact]
    public async Task Documento_openapi_corresponde_ao_snapshot_versionado()
    {
        var atual = JsonNode.Parse(await _client.GetStringAsync("/openapi/v2.json"))!;
        var caminho = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/openapi/portal-tarefas_v2.json");
        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(caminho))!;
        atual["servers"] = null; snapshot["servers"] = null;
        Assert.True(JsonNode.DeepEquals(snapshot, atual), "O contrato OpenAPI mudou em relação ao snapshot versionado.");
    }
}

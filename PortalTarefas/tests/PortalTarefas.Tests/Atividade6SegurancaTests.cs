using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PortalTarefas.Tests;

/// <summary>Autenticação real por cookie (Identity) + matriz de autorização 401/403/200.</summary>
public class Atividade6SegurancaTests(PortalWebFactory factory) : IClassFixture<PortalWebFactory>
{
    // Ids do seed: 2 = "Montar pipeline HTTP" (Backend), 8 = "Montar telas React" (Frontend)
    private const int TarefaBackend = 2, TarefaFrontend = 8;

    /// <summary>Cliente "navegador": guarda cookies e repete o token antiforgery no cabeçalho.</summary>
    private sealed class Navegador(HttpClient http)
    {
        public HttpClient Http { get; } = http;
        public string? Xsrf { get; private set; }

        public async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string url, object? corpo = null)
        {
            var req = new HttpRequestMessage(metodo, url) { Content = corpo is null ? null : JsonContent.Create(corpo) };
            if (Xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", Xsrf);
            var resp = await Http.SendAsync(req);
            if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var c in cookies.Where(c => c.StartsWith("XSRF-TOKEN=")))
                    Xsrf = Uri.UnescapeDataString(c.Split(';')[0]["XSRF-TOKEN=".Length..]);
            return resp;
        }

        public async Task<HttpResponseMessage> Entrar(string email, string senha)
        {
            await Enviar(HttpMethod.Get, "/api/v2/auth/sessao");
            return await Enviar(HttpMethod.Post, "/api/v2/auth/login", new { email, senha });
        }
    }

    private Navegador NovoNavegador() => new(factory.CriarCliente(perfil: null));

    [Fact]
    public async Task Login_invalido_nao_revela_se_a_conta_existe()
    {
        var nav = NovoNavegador();
        var senhaErrada = await nav.Entrar("ana@portal.local", "senha-errada-123!");
        var contaInexistente = await nav.Entrar("ninguem@portal.local", "qualquer-senha-123!");
        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, contaInexistente.StatusCode);
        var a = await senhaErrada.Content.ReadFromJsonAsync<JsonElement>();
        var b = await contaInexistente.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_valido_emite_cookie_seguro_e_sessao()
    {
        var nav = NovoNavegador();
        var resp = await nav.Entrar("ana@portal.local", factory.SenhaUsuarios);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cookie = resp.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("portal.sessao="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var sessao = await (await nav.Enviar(HttpMethod.Get, "/api/v2/auth/sessao")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sessao.GetProperty("autenticado").GetBoolean());
        Assert.Equal("Backend", sessao.GetProperty("equipe").GetString());
    }

    [Fact]
    public async Task Login_sem_token_antiforgery_e_rejeitado()
    {
        var http = factory.CriarCliente(perfil: null);
        var resp = await http.PostAsJsonAsync("/api/v2/auth/login", new { email = "ana@portal.local", senha = factory.SenhaUsuarios });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Anonimo_recebe_401_na_api_e_redirecionamento_no_mvc()
    {
        var http = factory.CriarCliente(perfil: null, seguirRedirecionamentos: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v2/tarefas")).StatusCode);
        var mvc = await http.GetAsync("/Tarefas");
        Assert.Equal(HttpStatusCode.Redirect, mvc.StatusCode);
        Assert.StartsWith("/Conta/Login", mvc.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Logout_invalida_cookie_copiado()
    {
        var nav = NovoNavegador();
        await nav.Entrar("bruno@portal.local", factory.SenhaUsuarios);
        Assert.Equal(HttpStatusCode.OK, (await nav.Enviar(HttpMethod.Get, "/api/v2/tarefas")).StatusCode);

        // "Rouba" o cookie de sessão antes do logout
        var resp = await nav.Enviar(HttpMethod.Get, "/api/v2/auth/sessao");
        var cookieCopiado = factory.Server.BaseAddress is not null ? await CapturarCookieSessao(nav) : null;

        Assert.Equal(HttpStatusCode.NoContent, (await nav.Enviar(HttpMethod.Post, "/api/v2/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await nav.Enviar(HttpMethod.Get, "/api/v2/tarefas")).StatusCode);

        // Reenvia o cookie antigo manualmente, em um cliente sem estado: o security stamp mudou -> 401
        var outro = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v2/tarefas");
        req.Headers.Add("Cookie", cookieCopiado!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await outro.SendAsync(req)).StatusCode);
    }

    private async Task<string> CapturarCookieSessao(Navegador nav)
    {
        // Refaz o login em um cliente sem gerenciamento automático para ler o valor bruto do cookie emitido.
        var cru = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        var sessao = await cru.GetAsync("/api/v2/auth/sessao");
        var cookies = sessao.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();
        var xsrf = Uri.UnescapeDataString(cookies.Single(c => c.StartsWith("XSRF-TOKEN="))["XSRF-TOKEN=".Length..]);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/auth/login")
        { Content = JsonContent.Create(new { email = "bruno@portal.local", senha = factory.SenhaUsuarios }) };
        req.Headers.Add("Cookie", string.Join("; ", cookies));
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        var login = await cru.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await Task.CompletedTask;
        // O logout feito pelo "navegador" troca o security stamp do usuário, invalidando também este cookie.
        return login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("portal.sessao=")).Split(';')[0];
    }

    [Theory]
    // perfil,      método,  alvo,              esperado
    [InlineData(null, "GET", "lista", HttpStatusCode.Unauthorized)]
    [InlineData("visitante", "GET", "lista", HttpStatusCode.OK)]
    [InlineData("visitante", "POST", "nova", HttpStatusCode.Forbidden)]
    [InlineData("ana", "POST", "nova", HttpStatusCode.Created)]
    [InlineData("ana", "PUT", "backend", HttpStatusCode.OK)]
    [InlineData("ana", "PUT", "frontend", HttpStatusCode.Forbidden)]
    [InlineData("bruno", "PUT", "frontend", HttpStatusCode.OK)]
    [InlineData("carla", "PUT", "frontend", HttpStatusCode.OK)]       // claim editar-outras-equipes
    [InlineData("bruno", "DELETE", "backend", HttpStatusCode.Forbidden)]
    [InlineData(null, "DELETE", "backend", HttpStatusCode.Unauthorized)]
    [InlineData("ana", "GET", "lista", HttpStatusCode.OK)]
    [InlineData("bruno", "GET", "lista", HttpStatusCode.OK)]
    [InlineData("carla", "GET", "lista", HttpStatusCode.OK)]
    [InlineData(null, "POST", "nova", HttpStatusCode.Unauthorized)]
    [InlineData("bruno", "POST", "nova", HttpStatusCode.Created)]
    [InlineData("carla", "POST", "nova", HttpStatusCode.Created)]
    [InlineData(null, "PUT", "backend", HttpStatusCode.Unauthorized)]
    [InlineData("visitante", "PUT", "backend", HttpStatusCode.Forbidden)]
    [InlineData("visitante", "PUT", "frontend", HttpStatusCode.Forbidden)]
    [InlineData("bruno", "PUT", "backend", HttpStatusCode.Forbidden)]
    [InlineData("carla", "PUT", "backend", HttpStatusCode.OK)]
    [InlineData(null, "PUT", "frontend", HttpStatusCode.Unauthorized)]
    [InlineData("visitante", "DELETE", "backend", HttpStatusCode.Forbidden)]
    [InlineData("ana", "DELETE", "backend", HttpStatusCode.Forbidden)]
    public async Task Matriz_de_autorizacao(string? perfil, string metodo, string alvo, HttpStatusCode esperado)
    {
        var http = factory.CriarCliente(perfil);
        var leitor = factory.CriarCliente("carla");
        HttpResponseMessage resp;
        switch (metodo)
        {
            case "GET": resp = await http.GetAsync("/api/v2/tarefas"); break;
            case "POST": resp = await http.PostAsJsonAsync("/api/v2/tarefas", new { titulo = $"Nova {perfil} {Guid.NewGuid():N}" }); break;
            case "PUT":
                var id = alvo == "backend" ? TarefaBackend : TarefaFrontend;
                var atual = await leitor.GetFromJsonAsync<JsonElement>($"/api/v2/tarefas/{id}");
                resp = await http.PutAsJsonAsync($"/api/v2/tarefas/{id}", new
                {
                    titulo = atual.GetProperty("titulo").GetString(), situacao = "EmAndamento", prioridade = atual.GetProperty("prioridade").GetString(),
                    prazo = "2030-01-01", versao = atual.GetProperty("versao").GetString()
                });
                break;
            default: resp = await http.DeleteAsync($"/api/v2/tarefas/{TarefaBackend}"); break;
        }
        Assert.Equal(esperado, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_exclui_tarefa()
    {
        var carla = factory.CriarCliente("carla");
        var criada = await carla.PostAsJsonAsync("/api/v2/tarefas", new { titulo = "Para excluir" });
        Assert.Equal(HttpStatusCode.NoContent, (await carla.DeleteAsync(criada.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task Mvc_tambem_aplica_regra_de_equipe()
    {
        var ana = factory.CriarCliente("ana", seguirRedirecionamentos: false);
        // Com o esquema de teste o Forbid responde 403 direto (com cookie, redirecionaria para /Conta/AcessoNegado).
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync($"/Tarefas/Edit/{TarefaFrontend}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync($"/Tarefas/Edit/{TarefaBackend}")).StatusCode);
    }

    [Fact]
    public async Task Bloqueio_apos_tentativas_e_auditoria_sem_senha()
    {
        var nav = NovoNavegador();
        for (var i = 0; i < 5; i++) await nav.Entrar("visitante@portal.local", "errada-123456!");
        var comSenhaCerta = await nav.Entrar("visitante@portal.local", factory.SenhaUsuarios);
        Assert.Equal(HttpStatusCode.Unauthorized, comSenhaCerta.StatusCode);   // bloqueado temporariamente

        await using var db = BancoTeste.CriarContexto(factory.Conexao);
        var eventos = await db.EventosSeguranca.Where(e => e.Usuario == "visitante@portal.local").ToListAsync();
        Assert.Contains(eventos, e => e.Detalhe == "bloqueado");
        Assert.DoesNotContain(eventos, e => (e.Detalhe ?? "").Contains(factory.SenhaUsuarios) || (e.Detalhe ?? "").Contains("errada"));
    }
}

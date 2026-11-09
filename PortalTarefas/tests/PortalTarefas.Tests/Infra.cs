using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortalTarefas.Web.Dados;

namespace PortalTarefas.Tests;

/// <summary>
/// Cada classe de teste recebe um banco SQL Server próprio e descartável
/// (PortalTarefas_Testes_&lt;guid&gt;), criado pelas migrations e removido ao final.
/// O servidor vem da variável PORTAL_SQL_TESTES (sem senha no código).
/// </summary>
public static class BancoTeste
{
    public static string NovaConexao()
    {
        var baseCs = Environment.GetEnvironmentVariable("PORTAL_SQL_TESTES")
            ?? throw new InvalidOperationException("Defina PORTAL_SQL_TESTES com a conexão do SQL Server de testes.");
        return new SqlConnectionStringBuilder(baseCs) { InitialCatalog = $"PortalTarefas_Testes_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public static PortalTarefasDbContext CriarContexto(string conexao) =>
        new(new DbContextOptionsBuilder<PortalTarefasDbContext>()
            .UseSqlServer(conexao)
            .UseAsyncSeeding((ctx, _, ct) => SementeDados.SemearAsync(ctx, ct))
            .UseSeeding((ctx, _) => SementeDados.Semear(ctx))
            .Options);
}

/// <summary>Aplicação completa em memória (TestServer) apontando para um banco de testes isolado.</summary>
public class PortalWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string Conexao { get; } = BancoTeste.NovaConexao();

    /// <summary>Senha aleatória gerada a cada execução para os usuários de teste (nunca versionada).</summary>
    public string SenhaUsuarios { get; } = $"T{Guid.NewGuid():N}!a1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");   // aplica migrations + seed na inicialização
        builder.UseSetting("ConnectionStrings:PortalTarefas", Conexao);
        builder.UseSetting("SeedUsuarios:SenhaInicial", SenhaUsuarios);
        builder.ConfigureTestServices(services =>
        {
            // Esquema de teste: com o cabeçalho X-Teste-Usuario a requisição é autenticada com um perfil fixo;
            // sem ele, segue o fluxo real de cookie do Identity.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TesteAuthHandler>(TesteAuthHandler.Esquema, _ => { })
                .AddPolicyScheme("TesteOuCookie", null, o => o.ForwardDefaultSelector = ctx =>
                    ctx.Request.Headers.ContainsKey(TesteAuthHandler.Cabecalho) ? TesteAuthHandler.Esquema : IdentityConstants.ApplicationScheme);
            services.PostConfigure<AuthenticationOptions>(o => o.DefaultScheme = "TesteOuCookie");
        });
    }

    /// <summary>Cliente HTTPS; com perfil (ana, bruno, carla, visitante) as requisições já saem autenticadas.</summary>
    public HttpClient CriarCliente(string? perfil = "carla", bool seguirRedirecionamentos = true)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = seguirRedirecionamentos
        });
        if (perfil is not null) c.DefaultRequestHeaders.Add(TesteAuthHandler.Cabecalho, perfil);
        return c;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using var ctx = BancoTeste.CriarContexto(Conexao);
        await ctx.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

/// <summary>Autenticação fictícia para testes, espelhando os usuários do seed.</summary>
public sealed class TesteAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string Esquema = "Teste";
    public const string Cabecalho = "X-Teste-Usuario";

    private static readonly Dictionary<string, (string? Papel, string Equipe, bool EditaOutras)> Perfis = new()
    {
        ["ana"] = ("Usuario", "Backend", false),
        ["bruno"] = ("Usuario", "Frontend", false),
        ["carla"] = ("Administrador", "Backend", true),
        ["visitante"] = (null, "Geral", false),
    };

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var nome = Request.Headers[Cabecalho].ToString();
        if (!Perfis.TryGetValue(nome, out var p)) return Task.FromResult(AuthenticateResult.Fail("perfil desconhecido"));
        var claims = new List<Claim> { new(ClaimTypes.Name, $"{nome}@portal.local"), new(ClaimTypes.NameIdentifier, nome), new("equipe", p.Equipe) };
        if (p.Papel is not null) claims.Add(new(ClaimTypes.Role, p.Papel));
        if (p.EditaOutras) claims.Add(new("permissao", "tarefas.editar-outras-equipes"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Esquema));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Esquema)));
    }
}

using Microsoft.AspNetCore.Hosting;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");   // aplica migrations + seed na inicialização
        builder.UseSetting("ConnectionStrings:PortalTarefas", Conexao);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using var ctx = BancoTeste.CriarContexto(Conexao);
        await ctx.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

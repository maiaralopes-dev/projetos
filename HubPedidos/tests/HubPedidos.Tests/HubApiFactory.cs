using HubPedidos.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests;

/// <summary>
/// API em memória com banco SQLite em memória compartilhado e exclusivo desta fábrica.
/// Ambiente "Testing": não carrega appsettings.Development (que semeia 20 mil pedidos).
/// </summary>
public sealed class HubApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=hub-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _ancora;

    public HubApiFactory()
    {
        // Mantém o banco vivo enquanto a fábrica existir.
        _ancora = new SqliteConnection(_connectionString);
        _ancora.Open();
    }

    public ContadorComandos Contador { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Hub", _connectionString);
        builder.UseSetting("Banco:AplicarMigrations", "true");
        builder.UseSetting("Banco:SemearPedidos", "0");
        builder.ConfigureServices(s => s.AddSingleton<IInterceptor>(Contador));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _ancora.Dispose();
        }
    }
}

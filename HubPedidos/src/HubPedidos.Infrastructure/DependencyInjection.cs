using HubPedidos.Application.Persistencia;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfraestrutura(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Hub")
            ?? throw new InvalidOperationException("ConnectionStrings:Hub não configurada.");
        // Trava para testes de integração: com Banco:ExigirEmMemoria=true, um connection
        // string que aponte para arquivo (ex.: o hubpedidos.db de desenvolvimento) derruba a
        // inicialização em vez de apagar/sujar dados reais.
        if (configuration.GetValue("Banco:ExigirEmMemoria", false)
            && !(connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase) || connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Banco:ExigirEmMemoria está ligado, mas ConnectionStrings:Hub não é um banco em memória ('{connectionString}').");
        }

        var mapeamento = configuration.GetValue("Banco:MapeamentoMetadados", MapeamentoMetadados.Json);

        services.AddSingleton(new OpcoesModeloHub(mapeamento));
        services.AddDbContext<HubDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            // Interceptores registrados no contêiner (ex.: ContadorComandos nos testes).
            options.AddInterceptors(sp.GetServices<IInterceptor>());
            // Nunca habilitar EnableSensitiveDataLogging fora de depuração local:
            // sem ele, os logs mostram os parâmetros como '?'.
        });

        services.AddScoped<IRepositorioPedidos, RepositorioPedidos>();
        services.AddScoped<IConsultasPedidos, ConsultasPedidos>();
        return services;
    }

    /// <summary>Aplica migrations e semeia dados conforme configuração (Banco:*).</summary>
    public static async Task PrepararBancoAsync(this IServiceProvider services, IConfiguration configuration)
    {
        await using var escopo = services.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HubDbContext>();

        if (configuration.GetValue("Banco:AplicarMigrations", false))
        {
            await db.Database.MigrateAsync();
        }

        // Atividade 5: no modo de journal padrão (rollback/"delete"), quem grava bloqueia
        // leitores e outros escritores; o Microsoft.Data.Sqlite responde a SQLITE_BUSY
        // dormindo (Thread.Sleep) e tentando de novo — o trace mostrou 43% do tempo das
        // threads nesse Sleep. Em WAL, leitores não bloqueiam o escritor e o commit é um
        // append sequencial. A configuração fica gravada no arquivo do banco.
        // synchronous continua FULL: a durabilidade não muda, só o modelo de concorrência.
        // DESLIGADO por padrão: medido na atividade 5, não trouxe ganho nem sozinho nem
        // junto com a serialização das escritas (o gargalo era escritor × escritor).
        if (configuration.GetValue("Banco:ModoWal", false) && db.Database.GetDbConnection().DataSource is not (null or "" or ":memory:"))
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        var semear = configuration.GetValue("Banco:SemearPedidos", 0);
        if (semear > 0 && !await db.Pedidos.AnyAsync())
        {
            await GeradorMassa.SemearAsync(db, semear);
        }
    }
}

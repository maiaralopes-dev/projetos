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

        var semear = configuration.GetValue("Banco:SemearPedidos", 0);
        if (semear > 0 && !await db.Pedidos.AnyAsync())
        {
            await GeradorMassa.SemearAsync(db, semear);
        }
    }
}

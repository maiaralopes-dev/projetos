using HubPedidos.Application.Importacao;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Módulo de pedidos: regras e contratos puros. Só as opções de importação são
    /// registradas aqui (singleton, imutáveis); as portas IRepositorioPedidos e
    /// IConsultasPedidos são implementadas e registradas pela infraestrutura.
    /// </summary>
    public static IServiceCollection AddModuloPedidos(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection("Importacao").Get<OpcoesImportacao>() ?? new OpcoesImportacao());
        return services;
    }
}

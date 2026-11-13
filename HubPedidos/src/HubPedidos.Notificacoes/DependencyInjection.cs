using HubPedidos.Notificacoes.TempoReal;
using HubPedidos.Processamento;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HubPedidos.Notificacoes;

public static class DependencyInjection
{
    /// <summary>
    /// SINGLETON: registros em memória (notificações, conexões, revogações), canais e o
    /// notificador composto — estado compartilhado por todas as requisições/conexões.
    /// SCOPED: AutorizadorPedidos (usa IConsultasPedidos → DbContext scoped).
    /// O hub é transiente por natureza (o SignalR cria um por invocação).
    /// </summary>
    public static IServiceCollection AddModuloNotificacoes(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<RegistroNotificacoes>();
        services.AddSingleton<RegistroConexoes>();
        services.AddSingleton<RegistroRevogacoes>();
        services.AddSingleton<ServicoRevogacao>();
        services.AddScoped<AutorizadorPedidos>();
        services.AddSingleton<ICanalNotificacao>(sp => sp.GetRequiredService<RegistroNotificacoes>());
        services.AddSingleton<ICanalNotificacao, CanalSignalR>();

        // Substitui o NotificadorNulo do processamento, qualquer que seja a ordem dos registros.
        services.RemoveAll<INotificadorPedidos>();
        services.AddSingleton<INotificadorPedidos, NotificadorComposto>();
        return services;
    }
}

using HubPedidos.Processamento.Fila;
using HubPedidos.Processamento.Processadores;
using HubPedidos.Processamento.Processadores.Implementacoes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HubPedidos.Processamento;

public sealed record OpcoesEscritaHttp(bool SerializarEscritas = true);

public static class DependencyInjection
{
    /// <summary>
    /// Registros do módulo de processamento, com tempos de vida justificados:
    /// <list type="bullet">
    /// <item>SINGLETON: catálogo (imutável, montado uma vez), fila/canal (estado compartilhado
    /// por todos os produtores e consumidores), limitador (o teto precisa ser global),
    /// métricas e opções (imutáveis).</item>
    /// <item>SCOPED: processadores (podem depender de serviços scoped, como DbContext) e o
    /// seletor (resolve no escopo de quem chamou).</item>
    /// <item>HOSTED (singleton): consumidor — usa IServiceScopeFactory para criar um escopo
    /// por item, em vez de capturar serviços scoped (dependência cativa).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddModuloProcessamento(this IServiceCollection services, IConfiguration configuration)
    {
        // Reflexão restrita: um assembly e um namespace conhecidos (atividade 1).
        var catalogo = CatalogoProcessadores.Construir(typeof(ProcessadorPadrao).Assembly, typeof(ProcessadorPadrao).Namespace!);
        services.AddSingleton(catalogo);
        foreach (var registro in catalogo.Registros)
        {
            services.AddKeyedScoped(typeof(IProcessadorPedido), registro.Categoria, registro.Tipo);
        }

        services.AddScoped<ISeletorProcessador, SeletorProcessadorKeyed>();

        var opcoesFila = (configuration.GetSection("Fila").Get<OpcoesFila>() ?? new OpcoesFila()).Validar();
        services.AddSingleton(opcoesFila);
        services.AddSingleton<MetricasFila>();
        services.AddSingleton<FilaPedidos>();
        services.AddSingleton(new LimitadorBanco(opcoesFila.LimiteBanco));
        services.AddHostedService<ConsumidorPedidos>();
        services.AddSingleton(new OpcoesEscritaHttp(configuration.GetValue("Banco:SerializarEscritasHttp", true)));

        // Sem o módulo de notificações, publicar é um no-op. TryAdd: quem registrar antes vence.
        services.TryAddSingleton<INotificadorPedidos, NotificadorNulo>();
        return services;
    }
}

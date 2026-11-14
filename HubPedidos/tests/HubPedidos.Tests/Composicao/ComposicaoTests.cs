using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Persistencia;
using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Notificacoes;
using HubPedidos.Processamento;
using HubPedidos.Processamento.Processadores;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests.Composicao;

/// <summary>Analisa o grafo de DI: um singleton não pode depender (no construtor) de um scoped.</summary>
internal static class AnalisadorDependenciaCativa
{
    public static IReadOnlyList<string> Encontrar(IServiceCollection servicos)
    {
        // Último registro vence (é o que o contêiner resolve para um único serviço).
        var tempoDeVida = servicos.Where(d => !d.IsKeyedService).GroupBy(d => d.ServiceType).ToDictionary(g => g.Key, g => g.Last().Lifetime);
        var violacoes = new List<string>();

        foreach (var d in servicos.Where(d => d.Lifetime == ServiceLifetime.Singleton))
        {
            var impl = d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType;
            if (impl is null)
            {
                continue; // instância pronta ou fábrica: analisadas em tempo de execução (ValidateScopes)
            }

            var ctor = impl.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            foreach (var p in ctor?.GetParameters() ?? [])
            {
                if (tempoDeVida.TryGetValue(p.ParameterType, out var vida) && vida == ServiceLifetime.Scoped)
                {
                    violacoes.Add($"{impl.Name} (singleton) recebe {p.ParameterType.Name} (scoped)");
                }
            }
        }

        return violacoes;
    }
}

public class ComposicaoTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public void Grafo_da_aplicacao_nao_tem_dependencia_cativa()
    {
        IServiceCollection? capturada = null;
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => capturada = s));
        _ = app.Services; // constrói o host (ValidateOnBuild + ValidateScopes ligados no Program)

        Assert.NotNull(capturada);
        Assert.Empty(AnalisadorDependenciaCativa.Encontrar(capturada!));
    }

    [Fact]
    public void Analisador_e_o_conteiner_detectam_uma_dependencia_cativa_proposital()
    {
        var servicos = new ServiceCollection();
        servicos.AddScoped<Escopado>();
        servicos.AddSingleton<Cativo>();

        Assert.Equal(["Cativo (singleton) recebe Escopado (scoped)"], AnalisadorDependenciaCativa.Encontrar(servicos));
        var ex = Assert.Throws<AggregateException>(() => servicos.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        Assert.Contains("Cannot consume scoped service", ex.Message);
    }

    [Fact]
    public void Fluxos_principais_sao_resolvidos_com_os_tempos_de_vida_esperados()
    {
        using var escopo = factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var catalogo = sp.GetRequiredService<CatalogoProcessadores>();
        var seletor = sp.GetRequiredService<ISeletorProcessador>();

        // Cada categoria do catálogo resolve para o tipo declarado pelo atributo.
        Assert.All(catalogo.Registros, r => Assert.IsType(r.Tipo, seletor.Obter(r.Categoria)));
        Assert.False(seletor.Suporta(CategoriaProcessamento.Invalido));

        Assert.IsType<RepositorioPedidos>(sp.GetRequiredService<IRepositorioPedidos>());
        Assert.IsType<ConsultasPedidos>(sp.GetRequiredService<IConsultasPedidos>());

        // O módulo de notificações substitui o padrão nulo por um composto de canais
        // (memória, SignalR e invalidação de cache da API).
        Assert.IsType<NotificadorComposto>(sp.GetRequiredService<INotificadorPedidos>());
        Assert.Equal(["CanalCaptura", "CanalInvalidacaoCache", "CanalSignalR", "RegistroNotificacoes"],
            sp.GetServices<ICanalNotificacao>().Select(c => c.GetType().Name).Order());

        // Scoped: mesmo escopo => mesma instância; escopos diferentes => instâncias diferentes.
        using var outro = factory.Services.CreateScope();
        Assert.Same(sp.GetRequiredService<HubDbContext>(), sp.GetRequiredService<HubDbContext>());
        Assert.NotSame(sp.GetRequiredService<HubDbContext>(), outro.ServiceProvider.GetRequiredService<HubDbContext>());
    }

    [Fact]
    public void Sem_o_modulo_de_notificacoes_o_processamento_usa_o_notificador_nulo()
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AddModuloProcessamento(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        using var sp = servicos.BuildServiceProvider();
        Assert.IsType<NotificadorNulo>(sp.GetRequiredService<INotificadorPedidos>());
    }

    private sealed class Escopado;

    private sealed class Cativo(Escopado escopado)
    {
        public Escopado Escopado { get; } = escopado;
    }
}

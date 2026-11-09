using System.Collections.Concurrent;
using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Persistencia;
using HubPedidos.Application.Processadores;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HubPedidos.Tests.Processamento;

/// <summary>
/// Repositório falso que mede a concorrência REAL das chamadas (independente do
/// LimitadorBanco) e pode bloquear (portão), atrasar ou falhar por pedido.
/// </summary>
internal sealed class RepositorioFalso : IRepositorioPedidos
{
    private int _dentro;
    private int _maximo;

    public ConcurrentDictionary<Guid, int> Gravacoes { get; } = new();

    public TaskCompletionSource Portao { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool UsarPortao { get; init; }

    public TimeSpan Atraso { get; init; }

    public HashSet<Guid> FalharPara { get; } = [];

    public int MaximoConcorrente => Volatile.Read(ref _maximo);

    public Task AdicionarAsync(Pedido pedido, CategoriaProcessamento categoria, CancellationToken ct) =>
        OperarAsync(pedido.Id.Valor, ct, () =>
        {
            if (FalharPara.Contains(pedido.Id.Valor))
            {
                throw new InvalidOperationException("falha simulada no banco");
            }

            Gravacoes.AddOrUpdate(pedido.Id.Valor, 1, (_, n) => n + 1);
        });

    public int ChamadasLote;

    public Task AdicionarLoteAsync(IReadOnlyList<(Pedido Pedido, CategoriaProcessamento Categoria)> pedidos, CancellationToken ct) =>
        OperarAsync(Guid.Empty, ct, () =>
        {
            Interlocked.Increment(ref ChamadasLote);
            if (pedidos.Any(p => FalharPara.Contains(p.Pedido.Id.Valor)))
            {
                throw new InvalidOperationException("falha simulada no lote (transação desfeita)");
            }

            foreach (var p in pedidos)
            {
                Gravacoes.AddOrUpdate(p.Pedido.Id.Valor, 1, (_, n) => n + 1);
            }
        });

    public async Task<int> ConcluirLoteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await OperarAsync(Guid.Empty, ct, () => { });
        return ids.Count;
    }

    public async Task<ResultadoAlteracaoStatus> AlterarStatusAsync(Guid id, StatusPedido novo, CancellationToken ct)
    {
        await OperarAsync(id, ct, () => { });
        return ResultadoAlteracaoStatus.Alterado;
    }

    private async Task OperarAsync(Guid id, CancellationToken ct, Action acao)
    {
        var atual = Interlocked.Increment(ref _dentro);
        int max;
        while (atual > (max = Volatile.Read(ref _maximo)) && Interlocked.CompareExchange(ref _maximo, atual, max) != max)
        {
        }

        try
        {
            if (UsarPortao)
            {
                await Portao.Task.WaitAsync(ct);
            }

            if (Atraso > TimeSpan.Zero)
            {
                await Task.Delay(Atraso, ct);
            }

            acao();
        }
        finally
        {
            Interlocked.Decrement(ref _dentro);
        }
    }
}

internal sealed class ProcessadorFalso : IProcessadorPedido
{
    public ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ResultadoProcessamento("Falso", 1, "ok"));
}

/// <summary>Monta fila + consumidor reais com dependências falsas, sem host HTTP.</summary>
internal sealed class Bancada : IAsyncDisposable
{
    private readonly ServiceProvider _provedor;

    public Bancada(OpcoesFila opcoes, RepositorioFalso? repositorio = null)
    {
        Opcoes = opcoes.Validar();
        Repositorio = repositorio ?? new RepositorioFalso();
        Fila = new FilaPedidos(Opcoes, Metricas);
        Limitador = new LimitadorBanco(Opcoes.LimiteBanco);

        var servicos = new ServiceCollection();
        servicos.AddScoped<IRepositorioPedidos>(_ => Repositorio);
        foreach (var categoria in Enum.GetValues<CategoriaProcessamento>())
        {
            servicos.AddKeyedScoped<IProcessadorPedido, ProcessadorFalso>(categoria);
        }

        _provedor = servicos.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Consumidor = new ConsumidorPedidos(Fila, Limitador, Metricas, _provedor.GetRequiredService<IServiceScopeFactory>(), Opcoes, NullLogger<ConsumidorPedidos>.Instance);
    }

    public OpcoesFila Opcoes { get; }

    public MetricasFila Metricas { get; } = new();

    public FilaPedidos Fila { get; }

    public LimitadorBanco Limitador { get; }

    public RepositorioFalso Repositorio { get; }

    public ConsumidorPedidos Consumidor { get; }

    public static Pedido NovoPedido() => Construtores.Pedido();

    public async Task<List<Guid>> EnfileirarAsync(int quantidade)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < quantidade; i++)
        {
            var pedido = NovoPedido();
            Assert.Equal(ResultadoEnfileiramento.Aceito, await Fila.EnfileirarAsync(pedido, CategoriaProcessamento.Padrao, default));
            ids.Add(pedido.Id.Valor);
        }

        return ids;
    }

    public Task IniciarAsync() => Consumidor.StartAsync(CancellationToken.None);

    public async Task PararAsync(TimeSpan? prazo = null)
    {
        using var cts = new CancellationTokenSource(prazo ?? TimeSpan.FromSeconds(10));
        await Consumidor.StopAsync(cts.Token);
    }

    /// <summary>Espera por condição com prazo (sem atraso fixo): falha com mensagem se não ocorrer.</summary>
    public static Task AguardarAsync(Func<bool> condicao, string descricao, int prazoMs = 5_000) =>
        AguardarAsync(() => Task.FromResult(condicao()), descricao, prazoMs);

    public static async Task AguardarAsync(Func<Task<bool>> condicao, string descricao, int prazoMs = 5_000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(prazoMs);
        while (!await condicao())
        {
            Assert.True(DateTime.UtcNow < limite, $"Tempo esgotado aguardando: {descricao}");
            await Task.Delay(5);
        }
    }

    /// <summary>Todo item aceito tem destino conhecido.</summary>
    public void AssertBalanco() =>
        Assert.Equal(Metricas.Aceitos, Metricas.Processados + Metricas.Falhas + Metricas.Abandonados + Metricas.Descartados + Fila.Profundidade + Metricas.EmAndamento);

    public async ValueTask DisposeAsync()
    {
        Consumidor.Dispose();
        await _provedor.DisposeAsync();
    }
}

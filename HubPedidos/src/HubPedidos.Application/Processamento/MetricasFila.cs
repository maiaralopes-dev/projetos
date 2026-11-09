using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using HubPedidos.Application.Classificacao;

namespace HubPedidos.Application.Processamento;

/// <summary>
/// Contadores da fila. Escalares usam Interlocked; contagem por categoria usa
/// ConcurrentDictionary.AddOrUpdate (atômico em relação ao dicionário). Nada de
/// "if (!ContainsKey) ...; d[k]++", que perde incrementos sob concorrência.
/// Também publica os mesmos valores em System.Diagnostics.Metrics (meter "HubPedidos.Fila"),
/// visíveis no dotnet-counters (atividade 5).
/// </summary>
public sealed class MetricasFila
{
    public const string NomeMeter = "HubPedidos.Fila";

    private readonly ConcurrentDictionary<CategoriaProcessamento, long> _processadosPorCategoria = new();
    private readonly Counter<long>? _contadorResultado;
    private readonly Histogram<double>? _espera;
    private readonly Histogram<double>? _duracao;
    private Func<int> _profundidade = () => 0;
    private long _aceitos, _rejeitadosCheia, _rejeitadosEncerrando, _descartados, _processados, _falhas, _abandonados;
    private int _emAndamento;

    public MetricasFila(IMeterFactory? meterFactory = null)
    {
        if (meterFactory is null)
        {
            return;
        }

        var meter = meterFactory.Create(NomeMeter);
        _contadorResultado = meter.CreateCounter<long>("hub.fila.itens", description: "Itens por resultado (aceito, rejeitado, descartado, processado, falha, abandonado)");
        _espera = meter.CreateHistogram<double>("hub.fila.espera", unit: "ms", description: "Tempo entre enfileirar e começar a processar");
        _duracao = meter.CreateHistogram<double>("hub.fila.processamento", unit: "ms", description: "Duração do processamento de um item");
        meter.CreateObservableGauge("hub.fila.profundidade", () => _profundidade(), description: "Itens aguardando na fila");
        meter.CreateObservableGauge("hub.fila.em_andamento", () => Volatile.Read(ref _emAndamento), description: "Itens sendo processados");
    }

    /// <summary>Gancho para experimentos/testes observarem cada item concluído.</summary>
    public Action<RegistroProcessamento>? Observador { get; set; }

    public long Aceitos => Interlocked.Read(ref _aceitos);
    public long RejeitadosFilaCheia => Interlocked.Read(ref _rejeitadosCheia);
    public long RejeitadosEncerrando => Interlocked.Read(ref _rejeitadosEncerrando);
    public long Descartados => Interlocked.Read(ref _descartados);
    public long Processados => Interlocked.Read(ref _processados);
    public long Falhas => Interlocked.Read(ref _falhas);
    public long Abandonados => Interlocked.Read(ref _abandonados);
    public int EmAndamento => Volatile.Read(ref _emAndamento);

    public IReadOnlyDictionary<string, long> ProcessadosPorCategoria =>
        _processadosPorCategoria.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    internal void ObservarProfundidade(Func<int> profundidade) => _profundidade = profundidade;

    internal void RegistrarEnfileiramento(ResultadoEnfileiramento resultado)
    {
        switch (resultado)
        {
            case ResultadoEnfileiramento.Aceito:
                Interlocked.Increment(ref _aceitos);
                break;
            case ResultadoEnfileiramento.RejeitadoFilaCheia:
                Interlocked.Increment(ref _rejeitadosCheia);
                break;
            case ResultadoEnfileiramento.RejeitadoEncerrando:
                Interlocked.Increment(ref _rejeitadosEncerrando);
                break;
        }

        _contadorResultado?.Add(1, new KeyValuePair<string, object?>("resultado", resultado.ToString()));
    }

    internal void RegistrarDescarte()
    {
        Interlocked.Increment(ref _descartados);
        _contadorResultado?.Add(1, new KeyValuePair<string, object?>("resultado", "Descartado"));
    }

    internal void RegistrarAbandono()
    {
        Interlocked.Increment(ref _abandonados);
        _contadorResultado?.Add(1, new KeyValuePair<string, object?>("resultado", "Abandonado"));
    }

    internal void Interrompido()
    {
        Interlocked.Decrement(ref _emAndamento);
        RegistrarAbandono();
    }

    internal void Iniciou(TimeSpan espera)
    {
        Interlocked.Increment(ref _emAndamento);
        _espera?.Record(espera.TotalMilliseconds);
    }

    internal void Concluiu(ProcessarPedidoJob job, TimeSpan espera, TimeSpan duracao, bool sucesso)
    {
        Interlocked.Decrement(ref _emAndamento);
        if (sucesso)
        {
            Interlocked.Increment(ref _processados);
            _processadosPorCategoria.AddOrUpdate(job.Categoria, 1, static (_, atual) => atual + 1);
        }
        else
        {
            Interlocked.Increment(ref _falhas);
        }

        _duracao?.Record(duracao.TotalMilliseconds);
        _contadorResultado?.Add(1, new KeyValuePair<string, object?>("resultado", sucesso ? "Processado" : "Falha"));
        Observador?.Invoke(new RegistroProcessamento(job.PedidoId, job.Categoria, espera, duracao, sucesso));
    }

    internal static TimeSpan Decorrido(long desde) => Stopwatch.GetElapsedTime(desde);
}

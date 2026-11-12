using System.Diagnostics;
using System.Threading.Channels;
using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Processamento.Fila;

/// <summary>
/// Canal limitado (bounded) compartilhado entre produtores (endpoints) e consumidores
/// (<see cref="ConsumidorPedidos"/>). Registrado como singleton.
/// </summary>
public sealed class FilaPedidos
{
    private readonly Channel<ProcessarPedidoJob> _canal;
    private readonly OpcoesFila _opcoes;
    private readonly MetricasFila _metricas;
    private volatile bool _encerrada;

    public FilaPedidos(OpcoesFila opcoes, MetricasFila metricas)
    {
        _opcoes = opcoes.Validar();
        _metricas = metricas;
        _canal = Channel.CreateBounded<ProcessarPedidoJob>(
            new BoundedChannelOptions(opcoes.Capacidade)
            {
                FullMode = opcoes.ModoCheio,
                SingleReader = opcoes.Consumidores == 1,
                SingleWriter = false,
                AllowSynchronousContinuations = false, // consumidor não roda na thread do produtor (requisição HTTP)
            },
            // Só é chamado nos modos Drop*: o item sai da fila sem ser processado.
            itemDropped: _ => metricas.RegistrarDescarte());
        metricas.ObservarProfundidade(() => _canal.Reader.Count);
    }

    public ChannelReader<ProcessarPedidoJob> Leitor => _canal.Reader;

    public int Profundidade => _canal.Reader.Count;

    public int Capacidade => _opcoes.Capacidade;

    public BoundedChannelFullMode ModoCheio => _opcoes.ModoCheio;

    public bool Encerrada => _encerrada;

    public async ValueTask<ResultadoEnfileiramento> EnfileirarAsync(Pedido pedido, CategoriaProcessamento categoria, CancellationToken cancellationToken)
    {
        var resultado = await TentarEscreverAsync(new ProcessarPedidoJob(pedido, categoria, Stopwatch.GetTimestamp()), cancellationToken);
        _metricas.RegistrarEnfileiramento(resultado);
        return resultado;
    }

    /// <summary>Encerramento: nenhum produtor entra mais; os consumidores terminam ao esvaziar.</summary>
    public void Completar()
    {
        _encerrada = true;
        _canal.Writer.TryComplete();
    }

    private async ValueTask<ResultadoEnfileiramento> TentarEscreverAsync(ProcessarPedidoJob job, CancellationToken cancellationToken)
    {
        // Caminho rápido. Nos modos Drop*, TryWrite sempre aceita (e pode descartar outro item).
        if (_canal.Writer.TryWrite(job))
        {
            return ResultadoEnfileiramento.Aceito;
        }

        if (_encerrada)
        {
            return ResultadoEnfileiramento.RejeitadoEncerrando;
        }

        // Modo Wait com fila cheia: rejeição explícita imediata...
        if (_opcoes.TempoMaximoEsperaMs == 0)
        {
            return ResultadoEnfileiramento.RejeitadoFilaCheia;
        }

        // ...ou espera limitada (backpressure), respeitando o cancelamento da requisição.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(_opcoes.TempoMaximoEsperaMs);
        try
        {
            await _canal.Writer.WriteAsync(job, limite.Token);
            return ResultadoEnfileiramento.Aceito;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ResultadoEnfileiramento.RejeitadoFilaCheia; // esgotou o tempo de espera
        }
        catch (ChannelClosedException)
        {
            return ResultadoEnfileiramento.RejeitadoEncerrando;
        }
    }
}

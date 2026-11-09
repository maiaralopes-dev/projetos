using HubPedidos.Application.Persistencia;
using HubPedidos.Application.Processadores;
using HubPedidos.Domain.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HubPedidos.Application.Processamento;

/// <summary>
/// N consumidores lendo o mesmo canal. Cada item ganha um escopo de DI próprio (DbContext
/// novo, nunca compartilhado entre threads). O semáforo envolve SÓ as operações de banco;
/// o processador roda fora dele para não segurar o slot com trabalho não relacionado.
/// </summary>
public sealed partial class ConsumidorPedidos(
    FilaPedidos fila,
    LimitadorBanco limitador,
    MetricasFila metricas,
    IServiceScopeFactory escopos,
    OpcoesFila opcoes,
    ILogger<ConsumidorPedidos> log) : BackgroundService
{
    private readonly CancellationTokenSource _pararLeitura = new();
    private Task _consumidores = Task.CompletedTask;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumidores = Task.WhenAll(Enumerable.Range(1, opcoes.Consumidores).Select(n => ConsumirAsync(n, stoppingToken)));
        return _consumidores;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        fila.Completar(); // produtores passam a receber RejeitadoEncerrando
        LogEncerrando(opcoes.Encerramento, fila.Profundidade);

        if (opcoes.Encerramento == PoliticaEncerramento.Abandonar)
        {
            await _pararLeitura.CancelAsync();
        }
        else
        {
            try
            {
                // Writer completado: ReadAllAsync termina quando a fila esvazia.
                await _consumidores.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LogPrazoEsgotado(fila.Profundidade);
            }
        }

        await base.StopAsync(cancellationToken); // cancela o stoppingToken (interrompe o que estiver em andamento)

        // Se o prazo já esgotou, base.StopAsync NÃO espera ExecuteTask (o token dele já
        // está cancelado). Damos um tempo curto e independente para os consumidores
        // observarem o cancelamento e registrarem os itens interrompidos.
        try
        {
            await _consumidores.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            LogPrazoEsgotado(fila.Profundidade);
        }

        while (fila.Leitor.TryRead(out _))
        {
            metricas.RegistrarAbandono();
        }
    }

    public override void Dispose()
    {
        _pararLeitura.Dispose();
        base.Dispose();
    }

    private async Task ConsumirAsync(int numero, CancellationToken stoppingToken)
    {
        await Task.Yield(); // não bloquear StartAsync do host
        using var leitura = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _pararLeitura.Token);
        try
        {
            await foreach (var job in fila.Leitor.ReadAllAsync(leitura.Token))
            {
                if (opcoes.TamanhoLote == 1)
                {
                    await ProcessarAsync(job, stoppingToken);
                    continue;
                }

                // Lote oportunista: junta só o que JÁ está na fila (TryRead não espera),
                // então com fila vazia a latência é a mesma do item a item.
                var lote = new List<ProcessarPedidoJob>(opcoes.TamanhoLote) { job };
                while (lote.Count < opcoes.TamanhoLote && fila.Leitor.TryRead(out var proximo))
                {
                    lote.Add(proximo);
                }

                await ProcessarLoteAsync(lote, stoppingToken);
            }
        }
        // O filtro olha os tokens de ORIGEM, não o "leitura" ligado: Cancel() executa os
        // callbacks em ordem inversa e de forma síncrona, então a exceção do item em
        // andamento pode chegar aqui ANTES de o token ligado ser marcado como cancelado.
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || _pararLeitura.IsCancellationRequested)
        {
        }

        LogConsumidorEncerrado(numero);
    }

    internal async Task ProcessarAsync(ProcessarPedidoJob job, CancellationToken cancellationToken)
    {
        var espera = MetricasFila.Decorrido(job.EnfileiradoEm);
        var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
        metricas.Iniciou(espera);
        var sucesso = false;
        var interrompido = false;

        using var _ = log.BeginScope(new Dictionary<string, object> { ["PedidoId"] = job.PedidoId });
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var repositorio = escopo.ServiceProvider.GetRequiredService<IRepositorioPedidos>();
            var processador = escopo.ServiceProvider.GetRequiredKeyedService<IProcessadorPedido>(job.Categoria);
            var pedido = job.Pedido with { Status = StatusPedido.EmProcessamento };

            await limitador.ExecutarAsync(ct => repositorio.AdicionarAsync(pedido, job.Categoria, ct), cancellationToken);
            var resultado = await processador.ProcessarAsync(pedido, cancellationToken); // fora do semáforo
            await limitador.ExecutarAsync(ct => repositorio.AlterarStatusAsync(job.PedidoId, StatusPedido.Concluido, ct), cancellationToken);

            sucesso = true;
            LogProcessado(job.PedidoId, resultado.Processador, MetricasFila.Decorrido(inicio).TotalMilliseconds, espera.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            interrompido = true;
            LogInterrompido(job.PedidoId);
            throw; // encerramento: o laço de leitura também termina
        }
        catch (Exception ex)
        {
            // Falha de UM item não derruba o consumidor: registra com o id e segue.
            LogFalha(ex, job.PedidoId, job.Categoria.ToString());
        }
        finally
        {
            if (interrompido)
            {
                metricas.Interrompido();
            }
            else
            {
                metricas.Concluiu(job, espera, MetricasFila.Decorrido(inicio), sucesso);
            }
        }
    }

    internal async Task ProcessarLoteAsync(List<ProcessarPedidoJob> lote, CancellationToken cancellationToken)
    {
        if (lote.Count == 1)
        {
            await ProcessarAsync(lote[0], cancellationToken);
            return;
        }

        var esperas = lote.Select(j => MetricasFila.Decorrido(j.EnfileiradoEm)).ToArray();
        var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
        await using var escopo = escopos.CreateAsyncScope();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IRepositorioPedidos>();

        try
        {
            var pedidos = lote.Select(j => (j.Pedido with { Status = StatusPedido.EmProcessamento }, j.Categoria)).ToList();
            await limitador.ExecutarAsync(ct => repositorio.AdicionarLoteAsync(pedidos, ct), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A transação do lote foi desfeita (nada gravado): reprocessa item a item para
            // isolar o item problemático, sem perder os demais.
            LogLoteFalhou(ex, lote.Count);
            foreach (var job in lote)
            {
                await ProcessarAsync(job, cancellationToken);
            }

            return;
        }

        var concluidos = new List<int>(lote.Count);
        var interrompido = false;
        for (var i = 0; i < lote.Count; i++)
        {
            metricas.Iniciou(esperas[i]);
        }

        try
        {
            for (var i = 0; i < lote.Count; i++)
            {
                try
                {
                    var processador = escopo.ServiceProvider.GetRequiredKeyedService<IProcessadorPedido>(lote[i].Categoria);
                    await processador.ProcessarAsync(lote[i].Pedido, cancellationToken); // fora do semáforo
                    concluidos.Add(i);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogFalha(ex, lote[i].PedidoId, lote[i].Categoria.ToString());
                }
            }

            var ids = concluidos.Select(i => lote[i].PedidoId).ToArray();
            await limitador.ExecutarAsync(ct => repositorio.ConcluirLoteAsync(ids, ct), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            interrompido = true;
            throw;
        }
        finally
        {
            var duracao = MetricasFila.Decorrido(inicio);
            for (var i = 0; i < lote.Count; i++)
            {
                if (interrompido)
                {
                    metricas.Interrompido();
                }
                else
                {
                    metricas.Concluiu(lote[i], esperas[i], duracao, concluidos.Contains(i));
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lote de {Quantidade} pedidos falhou ao gravar; reprocessando item a item")]
    private partial void LogLoteFalhou(Exception ex, int quantidade);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fila encerrando (política {Politica}), {Pendentes} item(ns) pendente(s)")]
    private partial void LogEncerrando(PoliticaEncerramento politica, int pendentes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prazo de drenagem esgotado com {Pendentes} item(ns) na fila")]
    private partial void LogPrazoEsgotado(int pendentes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumidor {Numero} encerrado")]
    private partial void LogConsumidorEncerrado(int numero);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pedido {PedidoId} processado por {Processador} em {DuracaoMs:F1} ms (espera na fila {EsperaMs:F1} ms)")]
    private partial void LogProcessado(Guid pedidoId, string processador, double duracaoMs, double esperaMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processamento do pedido {PedidoId} interrompido pelo encerramento")]
    private partial void LogInterrompido(Guid pedidoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao processar o pedido {PedidoId} (categoria {Categoria})")]
    private partial void LogFalha(Exception ex, Guid pedidoId, string categoria);
}

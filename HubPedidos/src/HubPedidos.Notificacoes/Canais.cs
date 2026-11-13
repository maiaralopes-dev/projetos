using HubPedidos.Processamento;
using Microsoft.Extensions.Logging;

namespace HubPedidos.Notificacoes;

public sealed record NotificacaoResponse(Guid PedidoId, string Status, string Categoria, DateTimeOffset Em);

/// <summary>
/// Um destino de notificação (memória, SignalR, invalidação de cache…). Porta declarada
/// AQUI, por quem consome; a API implementa o canal de invalidação de cache.
/// </summary>
public interface ICanalNotificacao
{
    ValueTask PublicarAsync(PedidoAtualizado evento, CancellationToken cancellationToken);
}

/// <summary>
/// Implementação da porta do Processamento: entrega o evento a todos os canais. A falha de
/// um canal é registrada e NÃO impede os demais (nem desfaz a mudança, já persistida).
/// </summary>
public sealed partial class NotificadorComposto(IEnumerable<ICanalNotificacao> canais, ILogger<NotificadorComposto> log) : INotificadorPedidos
{
    private readonly ICanalNotificacao[] _canais = [.. canais];

    public async ValueTask PublicarAsync(PedidoAtualizado evento, CancellationToken cancellationToken)
    {
        foreach (var canal in _canais)
        {
            try
            {
                await canal.PublicarAsync(evento, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCanalFalhou(ex, canal.GetType().Name, evento.PedidoId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canal {Canal} falhou ao notificar o pedido {PedidoId}")]
    private partial void LogCanalFalhou(Exception ex, string canal, Guid pedidoId);
}

/// <summary>Guarda as últimas N notificações em memória (consultáveis por HTTP).</summary>
public sealed class RegistroNotificacoes(int capacidade = 200) : ICanalNotificacao
{
    private readonly Queue<NotificacaoResponse> _recentes = new();
    private readonly Lock _trava = new();

    public RegistroNotificacoes() : this(200)
    {
    }

    public ValueTask PublicarAsync(PedidoAtualizado evento, CancellationToken cancellationToken)
    {
        var notificacao = new NotificacaoResponse(evento.PedidoId, evento.Status.ToString(), evento.Categoria.ToString(), evento.Em);
        lock (_trava) // enfileirar + aparar precisa ser uma operação só
        {
            _recentes.Enqueue(notificacao);
            while (_recentes.Count > capacidade)
            {
                _recentes.Dequeue();
            }
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<NotificacaoResponse> Recentes(int maximo = 50)
    {
        lock (_trava)
        {
            return [.. _recentes.Reverse().Take(maximo)];
        }
    }
}

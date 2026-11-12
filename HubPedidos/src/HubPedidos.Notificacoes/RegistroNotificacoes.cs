using HubPedidos.Processamento;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HubPedidos.Notificacoes;

public sealed record NotificacaoResponse(Guid PedidoId, string Status, string Categoria, DateTimeOffset Em);

/// <summary>
/// Adaptador do módulo de notificações para a porta <see cref="INotificadorPedidos"/>.
/// Nesta etapa guarda as últimas N notificações em memória (consultáveis por HTTP);
/// na atividade 7 passa a publicar também via SignalR, sem mudar o processamento.
/// </summary>
public sealed class RegistroNotificacoes(int capacidade = 200) : INotificadorPedidos
{
    private readonly Queue<NotificacaoResponse> _recentes = new();
    private readonly Lock _trava = new();

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

public static class DependencyInjection
{
    public static IServiceCollection AddModuloNotificacoes(this IServiceCollection services)
    {
        services.AddSingleton<RegistroNotificacoes>();
        // Substitui o NotificadorNulo do módulo de processamento, qualquer que seja a ordem dos registros.
        services.RemoveAll<INotificadorPedidos>();
        services.AddSingleton<INotificadorPedidos>(sp => sp.GetRequiredService<RegistroNotificacoes>());
        return services;
    }
}

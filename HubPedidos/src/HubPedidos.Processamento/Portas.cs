using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Processamento.Processadores;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Processamento;

/// <summary>Evento publicado quando o status de um pedido muda (dados mínimos, sem entidade).</summary>
public sealed record PedidoAtualizado(Guid PedidoId, string ClienteId, StatusPedido Status, CategoriaProcessamento Categoria, DateTimeOffset Em);

/// <summary>
/// PORTA declarada por quem CONSOME (o processamento precisa avisar que algo mudou).
/// Quem implementa é o módulo de Notificações, que depende deste módulo — nunca o contrário.
/// </summary>
public interface INotificadorPedidos
{
    ValueTask PublicarAsync(PedidoAtualizado evento, CancellationToken cancellationToken);
}

/// <summary>Padrão quando o módulo de notificações não está presente.</summary>
public sealed class NotificadorNulo : INotificadorPedidos
{
    public ValueTask PublicarAsync(PedidoAtualizado evento, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>Obtém o processador de uma categoria sem que o chamador use IServiceProvider.</summary>
public interface ISeletorProcessador
{
    bool Suporta(CategoriaProcessamento categoria);

    IProcessadorPedido Obter(CategoriaProcessamento categoria);
}

/// <summary>
/// ÚNICO ponto que resolve serviço manualmente (keyed services têm chave dinâmica). Fica
/// confinado aqui, na fronteira de composição do módulo, e é scoped: resolve no escopo
/// de quem o recebeu (requisição HTTP ou item da fila).
/// </summary>
internal sealed class SeletorProcessadorKeyed(IServiceProvider escopo, CatalogoProcessadores catalogo) : ISeletorProcessador
{
    public bool Suporta(CategoriaProcessamento categoria) =>
        categoria is not CategoriaProcessamento.Invalido && catalogo.TentarObter(categoria, out _);

    public IProcessadorPedido Obter(CategoriaProcessamento categoria) =>
        Suporta(categoria)
            ? escopo.GetRequiredKeyedService<IProcessadorPedido>(categoria)
            : throw new InvalidOperationException($"Nenhum processador para a categoria {categoria}.");
}

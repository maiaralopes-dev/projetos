using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HubPedidos.Notificacoes.TempoReal;

/// <summary>Contrato do evento enviado aos clientes: dados mínimos, nada do domínio.</summary>
/// <param name="Sequencia">Crescente por processo: o cliente descarta duplicatas e eventos fora de ordem.</param>
public sealed record PedidoAtualizadoMensagem(Guid PedidoId, string Status, long Sequencia, DateTimeOffset Em);

public sealed record AcessoRevogadoMensagem(string Motivo);

/// <summary>Métodos que o SERVIDOR chama no cliente (hub fortemente tipado: sem strings mágicas).</summary>
public interface IClientePedidos
{
    Task PedidoAtualizado(PedidoAtualizadoMensagem mensagem);

    Task AcessoRevogado(AcessoRevogadoMensagem mensagem);
}

public static class Grupos
{
    public const string Operadores = "operadores";

    public static string Pedido(Guid id) => $"pedido:{id:N}";

    public static string Cliente(string clienteId) => $"cliente:{clienteId}";
}

/// <summary>Conexões vivas e grupos de cada uma — necessário para revogar acesso.</summary>
public sealed class RegistroConexoes
{
    private readonly ConcurrentDictionary<string, (string? Cliente, ConcurrentDictionary<string, byte> Grupos)> _conexoes = new();

    public void Registrar(string conexao, string? cliente) => _conexoes[conexao] = (cliente, new());

    public void Remover(string conexao) => _conexoes.TryRemove(conexao, out _);

    public void AdicionarGrupo(string conexao, string grupo)
    {
        if (_conexoes.TryGetValue(conexao, out var c))
        {
            c.Grupos.TryAdd(grupo, 0);
        }
    }

    public void RemoverGrupo(string conexao, string grupo)
    {
        if (_conexoes.TryGetValue(conexao, out var c))
        {
            c.Grupos.TryRemove(grupo, out _);
        }
    }

    public IReadOnlyList<(string Conexao, string[] Grupos)> DoCliente(string cliente) =>
        [.. _conexoes.Where(kv => kv.Value.Cliente == cliente).Select(kv => (kv.Key, kv.Value.Grupos.Keys.ToArray()))];
}

/// <summary>
/// Hub de pedidos. [Authorize]: sem token válido, a negociação já é recusada (401).
/// Grupo NÃO é mecanismo de segurança: toda entrada em grupo passa antes pelo
/// <see cref="AutorizadorPedidos"/>, inclusive na reconexão (o cliente chama de novo).
/// </summary>
[Authorize]
public sealed class PedidosHub(AutorizadorPedidos autorizador, RegistroConexoes conexoes, RegistroRevogacoes revogacoes) : Hub<IClientePedidos>
{
    public override async Task OnConnectedAsync()
    {
        var cliente = Context.User.ClienteId();
        if (!Context.User.EhOperador() && (cliente is null || revogacoes.EstaRevogado(cliente)))
        {
            Context.Abort(); // sem identidade de cliente ou acesso revogado: nem mantém a conexão
            return;
        }

        conexoes.Registrar(Context.ConnectionId, cliente);
        if (Context.User.EhOperador())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Operadores);
            conexoes.AdicionarGrupo(Context.ConnectionId, Grupos.Operadores);
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        conexoes.Remover(Context.ConnectionId); // o SignalR já tira a conexão dos grupos
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Autoriza no servidor e só então associa a conexão ao grupo do pedido.</summary>
    public async Task AssinarPedido(Guid pedidoId)
    {
        if (!await autorizador.PodeVerAsync(Context.User, pedidoId, Context.ConnectionAborted))
        {
            throw new HubException("Acesso negado ao pedido."); // mesma mensagem p/ inexistente e alheio
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Pedido(pedidoId));
        conexoes.AdicionarGrupo(Context.ConnectionId, Grupos.Pedido(pedidoId));
    }

    public async Task CancelarAssinatura(Guid pedidoId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Grupos.Pedido(pedidoId));
        conexoes.RemoverGrupo(Context.ConnectionId, Grupos.Pedido(pedidoId));
    }
}

/// <summary>Canal SignalR: publica DEPOIS da mudança persistida (quem chama já gravou).</summary>
public sealed class CanalSignalR(IHubContext<PedidosHub, IClientePedidos> hub) : ICanalNotificacao
{
    private long _sequencia;

    public Task PublicarAsync(Processamento.PedidoAtualizado evento, CancellationToken cancellationToken) =>
        hub.Clients.Groups(Grupos.Pedido(evento.PedidoId), Grupos.Operadores)
            .PedidoAtualizado(new PedidoAtualizadoMensagem(evento.PedidoId, evento.Status.ToString(), Interlocked.Increment(ref _sequencia), evento.Em));

    ValueTask ICanalNotificacao.PublicarAsync(Processamento.PedidoAtualizado evento, CancellationToken cancellationToken) =>
        new(PublicarAsync(evento, cancellationToken));
}

/// <summary>Revogação: marca o cliente, tira as conexões dele de todos os grupos e avisa.</summary>
public sealed class ServicoRevogacao(RegistroRevogacoes revogacoes, RegistroConexoes conexoes, IHubContext<PedidosHub, IClientePedidos> hub)
{
    public async Task<int> RevogarAsync(string clienteId, CancellationToken cancellationToken)
    {
        revogacoes.Revogar(clienteId);
        var afetadas = conexoes.DoCliente(clienteId);
        foreach (var (conexao, grupos) in afetadas)
        {
            foreach (var grupo in grupos)
            {
                await hub.Groups.RemoveFromGroupAsync(conexao, grupo, cancellationToken);
                conexoes.RemoverGrupo(conexao, grupo);
            }

            await hub.Clients.Client(conexao).AcessoRevogado(new AcessoRevogadoMensagem("Acesso revogado por um operador."));
        }

        return afetadas.Count;
    }

    public void Restaurar(string clienteId) => revogacoes.Restaurar(clienteId);
}

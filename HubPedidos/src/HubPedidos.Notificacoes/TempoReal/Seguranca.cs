using System.Collections.Concurrent;
using System.Security.Claims;
using HubPedidos.Application.Persistencia;

namespace HubPedidos.Notificacoes.TempoReal;

public static class Papeis
{
    public const string ClaimCliente = "cliente_id";
    public const string Operador = "operador";

    public static string? ClienteId(this ClaimsPrincipal? usuario) => usuario?.FindFirst(ClaimCliente)?.Value;

    public static bool EhOperador(this ClaimsPrincipal? usuario) => usuario?.IsInRole(Operador) == true;
}

/// <summary>Clientes com acesso revogado (em memória: vale para esta instância).</summary>
public sealed class RegistroRevogacoes
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revogados = new(StringComparer.Ordinal);

    public bool EstaRevogado(string? clienteId) => clienteId is not null && _revogados.ContainsKey(clienteId);

    public void Revogar(string clienteId) => _revogados[clienteId] = DateTimeOffset.UtcNow;

    public void Restaurar(string clienteId) => _revogados.TryRemove(clienteId, out _);
}

/// <summary>
/// Regra ÚNICA de acesso a um pedido, usada pelo hub E pela consulta HTTP v2:
/// operador vê tudo; cliente só vê os próprios pedidos; revogado não vê nada.
/// Pedido inexistente e pedido alheio dão a mesma resposta (não revela quais ids existem).
/// </summary>
public sealed class AutorizadorPedidos(IConsultasPedidos consultas, RegistroRevogacoes revogacoes)
{
    public async Task<bool> PodeVerAsync(ClaimsPrincipal? usuario, Guid pedidoId, CancellationToken cancellationToken)
    {
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (usuario.EhOperador())
        {
            return true;
        }

        var cliente = usuario.ClienteId();
        if (cliente is null || revogacoes.EstaRevogado(cliente))
        {
            return false;
        }

        var pedido = await consultas.ObterResumoAsync(pedidoId, cancellationToken);
        return pedido is not null && string.Equals(pedido.ClienteId, cliente, StringComparison.Ordinal);
    }
}

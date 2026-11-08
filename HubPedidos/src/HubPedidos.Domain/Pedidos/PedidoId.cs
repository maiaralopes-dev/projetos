using System.Diagnostics.CodeAnalysis;

namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Value object do identificador do pedido. É um <c>readonly record struct</c>:
/// sem alocação no heap, igualdade por valor e imutável.
/// Implementa <see cref="IParsable{TSelf}"/> para que a conversão a partir de texto
/// (rota, query, DTO) seja explícita e possa falhar sem exceção (TryParse).
/// </summary>
public readonly record struct PedidoId : IParsable<PedidoId>
{
    private PedidoId(Guid valor) => Valor = valor;

    public Guid Valor { get; }

    /// <summary>default(PedidoId) é representável (é um struct), mas nunca é válido.</summary>
    public bool EhValido => Valor != Guid.Empty;

    /// <summary>Gera um id ordenável no tempo (UUID v7), útil para índices futuros.</summary>
    public static PedidoId Novo() => new(Guid.CreateVersion7());

    public static PedidoId De(Guid valor) =>
        valor == Guid.Empty
            ? throw new DomainException("PedidoId não pode ser Guid.Empty.")
            : new PedidoId(valor);

    public static PedidoId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var id)
            ? id
            : throw new FormatException($"'{s}' não é um PedidoId válido.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PedidoId result)
    {
        if (Guid.TryParse(s, provider, out var guid) && guid != Guid.Empty)
        {
            result = new PedidoId(guid);
            return true;
        }

        result = default;
        return false;
    }

    public override string ToString() => Valor.ToString("D");
}

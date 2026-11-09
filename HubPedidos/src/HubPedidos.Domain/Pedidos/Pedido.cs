using System.Collections.Immutable;

namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Raiz de agregado do pedido.
/// <list type="bullet">
/// <item>É um <c>sealed record</c> para ganhar cópia não destrutiva (<c>with</c>) e
/// ToString legível, mas a igualdade foi sobrescrita para <b>identidade</b> (Id):
/// um pedido é uma entidade, não um value object.</item>
/// <item>Os itens são <see cref="ImmutableArray{T}"/>: a coleção não pode ser alterada
/// depois de criada (imutabilidade profunda, pois <see cref="ItemPedido"/> também é imutável).</item>
/// </list>
/// </summary>
public sealed record Pedido
{
    public const int MaximoItens = 100;
    public const int TamanhoMaximoCliente = 64;

    private Pedido(PedidoId id, string clienteId, Regiao regiao, Prioridade prioridade, ImmutableArray<ItemPedido> itens)
    {
        Id = id;
        ClienteId = clienteId;
        Regiao = regiao;
        Prioridade = prioridade;
        Itens = itens;
    }

    public static Pedido Criar(
        string clienteId,
        Regiao regiao,
        Prioridade prioridade,
        IEnumerable<ItemPedido> itens,
        Endereco? entrega = null,
        MetadadosPedido? metadados = null,
        DateTimeOffset? criadoEm = null) =>
        new(PedidoId.Novo(), clienteId, regiao, prioridade, [.. itens])
        {
            Entrega = entrega,
            Metadados = metadados ?? MetadadosPedido.Padrao,
            CriadoEm = criadoEm ?? DateTimeOffset.UtcNow,
        };

    public static Pedido Reconstituir(PedidoId id, string clienteId, Regiao regiao, Prioridade prioridade, IEnumerable<ItemPedido> itens) =>
        new(id, clienteId, regiao, prioridade, [.. itens]);

    public PedidoId Id
    {
        get;
        init => field = value.EhValido ? value : throw new DomainException("Pedido exige um PedidoId válido.");
    }

    public string ClienteId
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) || value.Length > TamanhoMaximoCliente
            ? throw new DomainException($"ClienteId deve ter entre 1 e {TamanhoMaximoCliente} caracteres.")
            : value.Trim();
    }

    public Regiao Regiao
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new DomainException($"Região '{(int)value}' inexistente.");
    }

    public Prioridade Prioridade
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new DomainException($"Prioridade '{(int)value}' inexistente.");
    }

    public ImmutableArray<ItemPedido> Itens
    {
        get;
        init => field = value switch
        {
            { IsDefaultOrEmpty: true } => throw new DomainException("Pedido deve ter ao menos um item."),
            { Length: > MaximoItens } => throw new DomainException($"Pedido aceita no máximo {MaximoItens} itens."),
            _ when value.Select(i => i.Sku).Distinct().Count() != value.Length
                => throw new DomainException("Pedido não pode repetir SKU; agrupe a quantidade."),
            _ => value,
        };
    }

    public StatusPedido Status
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new DomainException($"Status '{(int)value}' inexistente.");
    } = StatusPedido.Recebido;

    public Endereco? Entrega { get; init; }

    public MetadadosPedido Metadados
    {
        get;
        init => field = value ?? throw new DomainException("Metadados não podem ser nulos.");
    } = MetadadosPedido.Padrao;

    public DateTimeOffset CriadoEm { get; init; } = DateTimeOffset.UtcNow;

    public decimal Total => Itens.Sum(i => i.Subtotal);

    public int QuantidadeUnidades => Itens.Sum(i => i.Quantidade);

    // Igualdade de entidade: por identidade, não por valor de todos os campos.
    public bool Equals(Pedido? other) => other is not null && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();
}

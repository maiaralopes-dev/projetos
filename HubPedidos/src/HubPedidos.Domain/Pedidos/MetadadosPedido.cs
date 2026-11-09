namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Metadados pequenos e opcionais do pedido. É o agregado usado na comparação
/// "colunas relacionais × coluna JSON" da atividade 3.
/// </summary>
public sealed record MetadadosPedido
{
    public static readonly MetadadosPedido Padrao = new("api");

    public MetadadosPedido(string canal, string? origem = null, string? campanha = null)
    {
        Canal = canal;
        Origem = origem;
        Campanha = campanha;
    }

    public string Canal
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) || value.Length > 20
            ? throw new DomainException("Canal deve ter entre 1 e 20 caracteres.")
            : value.Trim().ToLowerInvariant();
    }

    public string? Origem
    {
        get;
        init => field = value is { Length: > 40 } ? throw new DomainException("Origem aceita até 40 caracteres.") : value;
    }

    public string? Campanha
    {
        get;
        init => field = value is { Length: > 40 } ? throw new DomainException("Campanha aceita até 40 caracteres.") : value;
    }
}

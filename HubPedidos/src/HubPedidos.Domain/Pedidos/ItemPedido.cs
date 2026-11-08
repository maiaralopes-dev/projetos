namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Item de pedido como value object (<c>sealed record</c>): dois itens com mesmo SKU,
/// quantidade e preço são iguais. As validações ficam nos acessores <c>init</c>
/// (palavra-chave <c>field</c> do C# 14), de modo que nem o construtor nem uma cópia
/// com <c>with</c> conseguem produzir um item inválido.
/// </summary>
public sealed record ItemPedido
{
    public const int QuantidadeMaxima = 1_000;
    public const decimal PrecoMaximo = 1_000_000m;
    public const int TamanhoMaximoSku = 32;

    public ItemPedido(string sku, int quantidade, decimal precoUnitario)
    {
        Sku = sku;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }

    public string Sku
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) || value.Length > TamanhoMaximoSku
            ? throw new DomainException($"SKU deve ter entre 1 e {TamanhoMaximoSku} caracteres.")
            : value.Trim().ToUpperInvariant();
    }

    public int Quantidade
    {
        get;
        init => field = value is > 0 and <= QuantidadeMaxima
            ? value
            : throw new DomainException($"Quantidade deve estar entre 1 e {QuantidadeMaxima}.");
    }

    public decimal PrecoUnitario
    {
        get;
        init => field = value is > 0m and <= PrecoMaximo && decimal.Round(value, 2) == value
            ? value
            : throw new DomainException($"Preço unitário deve ser positivo, até {PrecoMaximo} e com no máximo 2 casas decimais.");
    }

    public decimal Subtotal => Quantidade * PrecoUnitario;
}

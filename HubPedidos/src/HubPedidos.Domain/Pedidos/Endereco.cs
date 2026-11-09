namespace HubPedidos.Domain.Pedidos;

/// <summary>Endereço de entrega (value object). Persistido como tipo complexo em colunas.</summary>
public sealed record Endereco
{
    public Endereco(string logradouro, string cidade, string uf, string cep)
    {
        Logradouro = logradouro;
        Cidade = cidade;
        Uf = uf;
        Cep = cep;
    }

    public string Logradouro
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) || value.Length > 120
            ? throw new DomainException("Logradouro deve ter entre 1 e 120 caracteres.")
            : value.Trim();
    }

    public string Cidade
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) || value.Length > 60
            ? throw new DomainException("Cidade deve ter entre 1 e 60 caracteres.")
            : value.Trim();
    }

    public string Uf
    {
        get;
        init => field = value is { Length: 2 } && char.IsAsciiLetter(value[0]) && char.IsAsciiLetter(value[1])
            ? value.ToUpperInvariant()
            : throw new DomainException("UF deve ter 2 letras.");
    }

    public string Cep
    {
        get;
        init => field = value is { Length: 8 } && value.All(char.IsAsciiDigit)
            ? value
            : throw new DomainException("CEP deve ter 8 dígitos.");
    }
}

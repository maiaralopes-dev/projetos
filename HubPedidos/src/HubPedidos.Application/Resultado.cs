using System.Collections.Immutable;

namespace HubPedidos.Application;

/// <summary>Resultado de uma conversão que pode falhar, sem usar exceção como fluxo normal.</summary>
public sealed record Resultado<T>
{
    private Resultado(T? valor, ImmutableArray<ErroValidacao> erros)
    {
        Valor = valor;
        Erros = erros;
    }

    public T? Valor { get; }

    public ImmutableArray<ErroValidacao> Erros { get; }

    public bool Sucesso => Erros.IsEmpty;

    public static Resultado<T> Ok(T valor) => new(valor, []);

    public static Resultado<T> Falha(IEnumerable<ErroValidacao> erros) => new(default, [.. erros]);
}

public sealed record ErroValidacao(string Campo, string Mensagem);

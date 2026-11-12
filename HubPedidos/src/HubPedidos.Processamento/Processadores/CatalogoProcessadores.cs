using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using HubPedidos.Application.Classificacao;

namespace HubPedidos.Processamento.Processadores;

public sealed record RegistroProcessador(CategoriaProcessamento Categoria, int Versao, Type Tipo);

public sealed class CatalogoInvalidoException(IReadOnlyList<string> erros)
    : Exception("Catálogo de processadores inválido:" + Environment.NewLine + string.Join(Environment.NewLine, erros.Select(e => " - " + e)))
{
    public IReadOnlyList<string> Erros { get; } = erros;
}

/// <summary>
/// Catálogo categoria → processador, construído UMA vez na inicialização por reflexão
/// restrita: apenas um assembly conhecido e um prefixo de namespace são inspecionados
/// (nada de AppDomain.GetAssemblies()). Qualquer inconsistência gera erro legível,
/// listando todos os problemas de uma vez.
/// </summary>
public sealed class CatalogoProcessadores
{
    private readonly FrozenDictionary<CategoriaProcessamento, RegistroProcessador> _porCategoria;

    private CatalogoProcessadores(ImmutableArray<RegistroProcessador> registros)
    {
        Registros = registros;
        _porCategoria = registros.ToFrozenDictionary(r => r.Categoria);
    }

    /// <summary>Registros ordenados por categoria: a saída é determinística.</summary>
    public ImmutableArray<RegistroProcessador> Registros { get; }

    public bool TentarObter(CategoriaProcessamento categoria, out RegistroProcessador registro) =>
        _porCategoria.TryGetValue(categoria, out registro!);

    public static CatalogoProcessadores Construir(Assembly assembly, string prefixoNamespace, bool exigirTodasCategorias = true)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixoNamespace);

        var erros = new List<string>();
        var candidatos = new List<RegistroProcessador>();

        var tipos = assembly.GetTypes()
            .Where(t => t.Namespace is { } ns && (ns == prefixoNamespace || ns.StartsWith(prefixoNamespace + ".", StringComparison.Ordinal)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var tipo in tipos)
        {
            var atributos = tipo.GetCustomAttributes<ProcessadorPedidoAttribute>(inherit: false).ToArray();
            if (atributos.Length == 0)
            {
                continue; // sem metadado: não participa do catálogo
            }

            var problema = tipo switch
            {
                { IsClass: false } => "não é uma classe",
                { IsAbstract: true } => "é abstrato",
                { ContainsGenericParameters: true } => "é genérico aberto",
                _ when !typeof(IProcessadorPedido).IsAssignableFrom(tipo) => $"não implementa {nameof(IProcessadorPedido)}",
                _ when tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0 => "não possui construtor público",
                _ => null,
            };

            if (problema is not null)
            {
                erros.Add($"{tipo.FullName} {problema}.");
                continue;
            }

            foreach (var atributo in atributos)
            {
                if (atributo.Categoria is CategoriaProcessamento.Invalido || !Enum.IsDefined(atributo.Categoria))
                {
                    erros.Add($"{tipo.FullName} declara categoria não processável '{atributo.Categoria}'.");
                }
                else if (atributo.Versao < 1)
                {
                    erros.Add($"{tipo.FullName} declara versão {atributo.Versao}; a versão deve ser >= 1.");
                }
                else
                {
                    candidatos.Add(new(atributo.Categoria, atributo.Versao, tipo));
                }
            }
        }

        // Mesma categoria e mesma versão em dois tipos = ambiguidade.
        foreach (var duplicado in candidatos.GroupBy(c => (c.Categoria, c.Versao)).Where(g => g.Count() > 1))
        {
            erros.Add($"Categoria {duplicado.Key.Categoria} v{duplicado.Key.Versao} declarada por mais de um tipo: "
                + string.Join(", ", duplicado.Select(d => d.Tipo.Name)) + ".");
        }

        // Para cada categoria vence a maior versão (permite evoluir sem apagar a anterior).
        var escolhidos = candidatos
            .GroupBy(c => c.Categoria)
            .Select(g => g.MaxBy(c => c.Versao)!)
            .OrderBy(r => r.Categoria)
            .ToImmutableArray();

        if (exigirTodasCategorias)
        {
            var faltantes = Enum.GetValues<CategoriaProcessamento>()
                .Where(c => c != CategoriaProcessamento.Invalido && escolhidos.All(r => r.Categoria != c));
            erros.AddRange(faltantes.Select(c => $"Nenhum processador para a categoria {c}."));
        }

        return erros.Count > 0 ? throw new CatalogoInvalidoException(erros) : new CatalogoProcessadores(escolhidos);
    }
}

using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCaseOrderer("HubPedidos.Tests.OrdemAleatoria", "HubPedidos.Tests")]
[assembly: TestCollectionOrderer("HubPedidos.Tests.ColecoesEmOrdemAleatoria", "HubPedidos.Tests")]

namespace HubPedidos.Tests;

/// <summary>
/// Embaralha a ordem dos testes (dentro de cada classe) e das coleções. A semente vem de
/// HUB_SEMENTE_TESTES (para reproduzir uma falha) ou é sorteada; ela é impressa como
/// mensagem de diagnóstico do xUnit em toda execução.
/// </summary>
public static class SementeTestes
{
    private static readonly Lazy<int> Valor = new(() =>
        int.TryParse(Environment.GetEnvironmentVariable("HUB_SEMENTE_TESTES"), out var s) ? s : Random.Shared.Next(1, int.MaxValue));

    public static int Atual => Valor.Value;

    public static IEnumerable<T> Embaralhar<T>(IEnumerable<T> itens, Func<T, string> chave, string escopo)
    {
        // Ordena por uma chave estável antes de embaralhar: mesma semente => mesma ordem.
        var lista = itens.OrderBy(chave, StringComparer.Ordinal).ToList();
        var rnd = new Random(Atual ^ Estavel(escopo));
        for (var i = lista.Count - 1; i > 0; i--)
        {
            var j = rnd.Next(i + 1);
            (lista[i], lista[j]) = (lista[j], lista[i]);
        }

        return lista;
    }

    // string.GetHashCode é aleatório por processo; para reprodutibilidade usamos um hash fixo (FNV-1a).
    private static int Estavel(string texto)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in texto)
            {
                h = (h ^ c) * 16777619;
            }

            return h;
        }
    }
}

public sealed class OrdemAleatoria(IMessageSink diagnostico) : ITestCaseOrderer
{
    private static int _anunciada;

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
    {
        if (Interlocked.Exchange(ref _anunciada, 1) == 0)
        {
            diagnostico.OnMessage(new DiagnosticMessage($"[ordem-aleatoria] semente={SementeTestes.Atual} (reproduza com HUB_SEMENTE_TESTES={SementeTestes.Atual})"));
        }

        var casos = testCases.ToList();
        var escopo = casos.FirstOrDefault()?.TestMethod.TestClass.Class.Name ?? "";
        return SementeTestes.Embaralhar(casos, c => c.UniqueID, escopo);
    }
}

public sealed class ColecoesEmOrdemAleatoria : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        SementeTestes.Embaralhar(testCollections, c => c.DisplayName, "colecoes");
}

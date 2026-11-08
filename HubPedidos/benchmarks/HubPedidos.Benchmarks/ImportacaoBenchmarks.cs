using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Custo de CPU e alocação do parsing. A origem é um MemoryStream de propósito: isola o
/// parser do disco/rede. Em produção a E/S tende a dominar (ver EscritaBenchmarks).
/// </summary>
[MemoryDiagnoser]
public class ImportacaoBenchmarks
{
    private byte[] _dados = [];
    private readonly ImportadorBaseline _baseline = new();
    private readonly ImportadorSpanChar _spanChar = new();
    private readonly ImportadorUtf8Pool _utf8 = new();
    private readonly ImportadorUtf8Pool _utf8Limpando = new(new OpcoesImportacao { LimparBuffer = true });

    [Params(100, 10_000)]
    public int Linhas { get; set; }

    [GlobalSetup]
    public void Preparar() => _dados = GeradorLote.GerarUtf8(Linhas, semente: 42, incluirInvalidas: true);

    [Benchmark(Baseline = true)]
    public Task<int> Baseline() => ConsumirAsync(_baseline);

    [Benchmark]
    public Task<int> SpanChar() => ConsumirAsync(_spanChar);

    [Benchmark]
    public Task<int> Utf8Pool() => ConsumirAsync(_utf8);

    [Benchmark]
    public Task<int> Utf8PoolComLimpeza() => ConsumirAsync(_utf8Limpando);

    private async Task<int> ConsumirAsync(IImportadorLote importador)
    {
        var itens = 0;
        await foreach (var r in importador.LerAsync(new MemoryStream(_dados, writable: false)))
        {
            itens += r.Pedido?.Itens?.Count ?? 0;
        }

        return itens;
    }
}

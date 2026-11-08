using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Flush por item × escrita agrupada, gravando 2.000 resultados NDJSON em arquivo real.
/// Aqui o custo dominante é E/S (uma chamada de sistema por flush), não CPU.
/// </summary>
[MemoryDiagnoser]
public class EscritaBenchmarks
{
    private const int Itens = 2_000;
    private string _arquivo = "";

    [Params(1, 16, 256)]
    public int ItensPorFlush { get; set; }

    [GlobalSetup]
    public void Preparar() => _arquivo = Path.Combine(Path.GetTempPath(), $"hub-bench-{Guid.NewGuid():N}.ndjson");

    [GlobalCleanup]
    public void Limpar() => File.Delete(_arquivo);

    [Benchmark]
    public async Task EscreverAsync()
    {
        await using var arquivo = new FileStream(_arquivo, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using var escritor = new EscritorNdjson(arquivo, ItensPorFlush);
        for (var i = 0; i < Itens; i++)
        {
            await escritor.EscreverAsync(new ResultadoImportacaoResponse(i, "aceito", "0198f0c2-7c1d-7000-8000-000000000000", "Padrao", null));
        }
    }
}

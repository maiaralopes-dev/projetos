# BenchmarkDotNet — execução 2 (resultado final, código final)

Comando: `dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*"`

## ImportacaoBenchmarks
```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Hub    : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Job=Hub  IterationCount=15  LaunchCount=1  
WarmupCount=3  

```
| Method             | Linhas | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0    | Allocated  | Alloc Ratio |
|------------------- |------- |------------:|-----------:|-----------:|------:|--------:|--------:|-----------:|------------:|
| **Baseline**           | **100**    |    **59.68 μs** |   **0.532 μs** |   **0.415 μs** |  **1.00** |    **0.01** |  **0.6104** |  **157.16 KB** |        **1.00** |
| SpanChar           | 100    |    45.71 μs |   0.900 μs |   0.703 μs |  0.77 |    0.01 |  0.2441 |   71.27 KB |        0.45 |
| Utf8Pool           | 100    |    45.43 μs |   1.613 μs |   1.509 μs |  0.76 |    0.03 |  0.1221 |   43.95 KB |        0.28 |
| Utf8PoolComLimpeza | 100    |    46.62 μs |   1.336 μs |   1.250 μs |  0.78 |    0.02 |  0.1221 |   43.95 KB |        0.28 |
|                    |        |             |            |            |       |         |         |            |             |
| **Baseline**           | **10000**  | **7,285.96 μs** | **349.572 μs** | **326.990 μs** |  **1.00** |    **0.06** | **54.6875** |   **14761 KB** |        **1.00** |
| SpanChar           | 10000  | 5,748.14 μs | 395.399 μs | 369.856 μs |  0.79 |    0.06 | 23.4375 | 6593.45 KB |        0.45 |
| Utf8Pool           | 10000  | 5,153.71 μs | 185.620 μs | 173.629 μs |  0.71 |    0.04 | 15.6250 | 4215.72 KB |        0.29 |
| Utf8PoolComLimpeza | 10000  | 4,769.41 μs |  60.179 μs |  56.291 μs |  0.66 |    0.03 | 15.6250 | 4215.72 KB |        0.29 |

## EscritaBenchmarks
```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Hub    : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Job=Hub  IterationCount=15  LaunchCount=1  
WarmupCount=3  

```
| Method        | ItensPorFlush | Mean       | Error     | StdDev    | Allocated |
|-------------- |-------------- |-----------:|----------:|----------:|----------:|
| **EscreverAsync** | **1**             | **5,042.5 μs** | **354.20 μs** | **331.32 μs** | **895.71 KB** |
| **EscreverAsync** | **16**            | **1,908.1 μs** | **184.92 μs** | **172.97 μs** | **178.14 KB** |
| **EscreverAsync** | **256**           |   **884.6 μs** |  **55.84 μs** |  **52.23 μs** | **160.29 KB** |

Aviso do BenchmarkDotNet: EscritaBenchmarks.EscreverAsync -> distribuição bimodal (mValue = 3.5).

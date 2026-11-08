# BenchmarkDotNet — execução 1 (antes do ajuste do encoder JSON; usada para medir variabilidade)

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Hub    : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Job=Hub  IterationCount=15  LaunchCount=1  
WarmupCount=3  

```
| Type                 | Method             | ItensPorFlush | Linhas | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0    | Allocated  | Alloc Ratio |
|--------------------- |------------------- |-------------- |------- |------------:|-----------:|-----------:|------:|--------:|--------:|-----------:|------------:|
| **EscritaBenchmarks**    | **EscreverAsync**      | **1**             | **?**      | **4,959.93 μs** | **492.553 μs** | **460.735 μs** |     **?** |       **?** |       **-** |  **895.76 KB** |           **?** |
|                      |                    |               |        |             |            |            |       |         |         |            |             |
| **EscritaBenchmarks**    | **EscreverAsync**      | **16**            | **?**      | **1,932.10 μs** | **127.624 μs** | **113.135 μs** |     **?** |       **?** |       **-** |   **178.1 KB** |           **?** |
|                      |                    |               |        |             |            |            |       |         |         |            |             |
| **ImportacaoBenchmarks** | **Baseline**           | **?**             | **100**    |    **65.55 μs** |   **3.114 μs** |   **2.913 μs** |  **1.00** |    **0.06** |  **0.6104** |  **157.16 KB** |        **1.00** |
| ImportacaoBenchmarks | SpanChar           | ?             | 100    |    46.30 μs |   0.342 μs |   0.267 μs |  0.71 |    0.03 |  0.2441 |   71.27 KB |        0.45 |
| ImportacaoBenchmarks | Utf8Pool           | ?             | 100    |    45.75 μs |   1.246 μs |   1.041 μs |  0.70 |    0.04 |  0.1221 |   43.95 KB |        0.28 |
| ImportacaoBenchmarks | Utf8PoolComLimpeza | ?             | 100    |    45.00 μs |   2.373 μs |   2.103 μs |  0.69 |    0.04 |  0.1221 |   43.95 KB |        0.28 |
|                      |                    |               |        |             |            |            |       |         |         |            |             |
| **EscritaBenchmarks**    | **EscreverAsync**      | **256**           | **?**      | **1,055.01 μs** |  **52.662 μs** |  **49.260 μs** |     **?** |       **?** |       **-** |  **160.29 KB** |           **?** |
|                      |                    |               |        |             |            |            |       |         |         |            |             |
| **ImportacaoBenchmarks** | **Baseline**           | **?**             | **10000**  | **6,752.18 μs** |  **88.722 μs** |  **69.268 μs** |  **1.00** |    **0.01** | **54.6875** |   **14761 KB** |        **1.00** |
| ImportacaoBenchmarks | SpanChar           | ?             | 10000  | 5,490.92 μs |  58.705 μs |  45.833 μs |  0.81 |    0.01 | 23.4375 | 6593.45 KB |        0.45 |
| ImportacaoBenchmarks | Utf8Pool           | ?             | 10000  | 5,190.83 μs |  76.770 μs |  68.054 μs |  0.77 |    0.01 | 15.6250 | 4215.72 KB |        0.29 |
| ImportacaoBenchmarks | Utf8PoolComLimpeza | ?             | 10000  | 5,282.40 μs | 221.524 μs | 207.214 μs |  0.78 |    0.03 | 15.6250 | 4215.72 KB |        0.29 |

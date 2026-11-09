# BenchmarkDotNet — consultas EF Core 10 / SQLite em memória (20.000 pedidos)

Comando: `dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*ConsultasBenchmarks*"`

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Hub    : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Job=Hub  IterationCount=15  LaunchCount=1  
WarmupCount=3  

```
| Method                     | Mean       | Error      | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------------------- |-----------:|-----------:|-----------:|------:|--------:|----------:|------------:|
| Offset_Pagina1             | 125.875 μs |  4.9013 μs |  4.0928 μs |     ? |       ? |  32.27 KB |           ? |
| Offset_Pagina900           | 583.478 μs | 45.2531 μs | 37.7884 μs |     ? |       ? |  32.34 KB |           ? |
| Cursor_Posicao900          | 138.158 μs |  0.3757 μs |  0.3138 μs |     ? |       ? |  33.29 KB |           ? |
|                            |            |            |            |       |         |           |             |
| PorId_Normal               |  39.910 μs |  1.5665 μs |  1.4653 μs |  1.00 |    0.05 |  12.11 KB |        1.00 |
| PorId_Compilada            |  25.234 μs |  0.2356 μs |  0.2203 μs |  0.63 |    0.02 |   4.13 KB |        0.34 |
| PorId_AdoNet               |   3.985 μs |  0.0322 μs |  0.0301 μs |  0.10 |    0.00 |   1.16 KB |        0.10 |
|                            |            |            |            |       |         |           |             |
| Relatorio_ConsultaUnica    | 967.011 μs | 26.3501 μs | 23.3587 μs |     ? |       ? | 259.86 KB |           ? |
| Relatorio_ConsultaDividida | 905.265 μs |  2.9520 μs |  2.6168 μs |     ? |       ? | 256.95 KB |           ? |

Avisos do BenchmarkDotNet: PorId_Normal -> distribuição possivelmente multimodal (mValue = 2.89).

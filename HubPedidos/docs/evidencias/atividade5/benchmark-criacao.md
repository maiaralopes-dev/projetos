# BenchmarkDotNet — caminho de CPU do POST e custo do LimitadorBanco

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Hub    : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Job=Hub  IterationCount=15  LaunchCount=1  
WarmupCount=3  

```
| Method               | Mean          | Error      | StdDev     | Ratio     | RatioSD   | Gen0   | Allocated | Alloc Ratio |
|--------------------- |--------------:|-----------:|-----------:|----------:|----------:|-------:|----------:|------------:|
| CaminhoCpuDoPost     | 1,260.0951 ns | 82.9539 ns | 77.5951 ns | 15,122.22 | 14,201.89 | 0.0114 |    1568 B |          NA |
| OperacaoDireta       |     0.1493 ns |  0.0997 ns |  0.0933 ns |      1.79 |      2.24 |      - |         - |          NA |
| OperacaoViaLimitador |    53.5115 ns |  2.3073 ns |  2.1582 ns |    642.18 |    601.80 |      - |         - |          NA |

Obs.: a coluna Ratio não tem significado aqui (a linha de base OperacaoDireta mede ~0,15 ns, perto de zero).

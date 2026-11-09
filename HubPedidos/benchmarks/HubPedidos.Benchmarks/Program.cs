using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

// Executar SEMPRE em Release e fora do depurador:
//   dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*"
var config = DefaultConfig.Instance
    .AddJob(Job.Default
        .WithWarmupCount(3)
        .WithIterationCount(15)
        .WithLaunchCount(1)
        .WithId("Hub"))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

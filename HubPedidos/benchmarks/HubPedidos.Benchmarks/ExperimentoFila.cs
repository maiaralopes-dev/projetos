using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Persistencia;
using HubPedidos.Processamento.Processadores;
using HubPedidos.Processamento;
using HubPedidos.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Experimento de backpressure (atividade 4): fila e consumidores REAIS, gravando em SQLite
/// em arquivo. Produtores simulam clientes HTTP concorrentes a uma taxa fixa acima da
/// capacidade de processamento. Uso:
///   dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- experimento-fila [saida.csv]
/// </summary>
public static class ExperimentoFila
{
    public sealed record Cenario(string Nome, int Capacidade, int Consumidores, int LimiteBanco, BoundedChannelFullMode Modo, int EsperaMs, int Lote = 1);

    private static readonly Cenario[] Cenarios =
    [
        new("A: base (cap 200, 4 cons, banco 1, Wait 100ms)", 200, 4, 1, BoundedChannelFullMode.Wait, 100),
        new("B: mais paralelismo (cap 200, 16 cons, banco 8)", 200, 16, 8, BoundedChannelFullMode.Wait, 100),
        new("C: fila grande (cap 10000, 4 cons, banco 1)", 10_000, 4, 1, BoundedChannelFullMode.Wait, 100),
        new("D: DropOldest (cap 200, 4 cons, banco 1)", 200, 4, 1, BoundedChannelFullMode.DropOldest, 0),
        new("E: rejeição imediata (cap 200, 4 cons, banco 1, Wait 0)", 200, 4, 1, BoundedChannelFullMode.Wait, 0),
        new("F: A + lote de até 32 itens por transação", 200, 4, 1, BoundedChannelFullMode.Wait, 100, Lote: 32),
    ];

    private const int TaxaPorSegundo = 1_500;
    private static readonly TimeSpan Duracao = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan TrabalhoExterno = TimeSpan.FromMilliseconds(2); // ex.: chamada a antifraude

    public static async Task ExecutarAsync(string[] args)
    {
        var csvLinhaTempo = args.Length > 1 ? args[1] : null;
        var linhaTempo = new StringBuilder("cenario,t_ms,profundidade,em_andamento,banco_em_uso,processados\n");
        Console.WriteLine($"Taxa oferecida: {TaxaPorSegundo}/s por {Duracao.TotalSeconds:F0}s; trabalho externo simulado {TrabalhoExterno.TotalMilliseconds} ms/item (fora do semáforo).");
        Console.WriteLine();

        // Aquecimento descartado: JIT, construção do modelo do EF, pool de conexões.
        await RodarAsync(Cenarios[0] with { Nome = "aquecimento" }, new StringBuilder());

        for (var rodada = 1; rodada <= 2; rodada++)
        {
            Console.WriteLine($"# Rodada {rodada}");
            foreach (var cenario in Cenarios)
            {
                var r = await RodarAsync(cenario, rodada == 1 ? linhaTempo : new StringBuilder());
                Console.WriteLine($"## {cenario.Nome}");
                Console.WriteLine(r);
                Console.WriteLine();
            }
        }

        DemonstrarCheckThenAct();

        if (csvLinhaTempo is not null)
        {
            await File.WriteAllTextAsync(csvLinhaTempo, linhaTempo.ToString());
            Console.WriteLine($"Linha do tempo salva em {csvLinhaTempo}");
        }
    }

    private static async Task<string> RodarAsync(Cenario c, StringBuilder linhaTempo)
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"hub-exp-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={arquivo}";
        await using (var db = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseSqlite(connectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        var tempoBanco = new ConcurrentBag<double>();
        var servicos = new ServiceCollection();
        servicos.AddDbContext<HubDbContext>(o => o.UseSqlite(connectionString));
        servicos.AddScoped<IRepositorioPedidos>(sp => new RepositorioCronometrado(new RepositorioPedidos(sp.GetRequiredService<HubDbContext>()), tempoBanco));
        servicos.AddScoped<ISeletorProcessador, SeletorExperimento>();

        await using var provedor = servicos.BuildServiceProvider();
        var opcoes = new OpcoesFila { Capacidade = c.Capacidade, Consumidores = c.Consumidores, LimiteBanco = c.LimiteBanco, ModoCheio = c.Modo, TempoMaximoEsperaMs = c.EsperaMs, TamanhoLote = c.Lote }.Validar();
        var metricas = new MetricasFila();
        var fila = new FilaPedidos(opcoes, metricas);
        var limitador = new LimitadorBanco(opcoes.LimiteBanco);
        var consumidor = new ConsumidorPedidos(fila, limitador, metricas, provedor.GetRequiredService<IServiceScopeFactory>(), new NotificadorNulo(), opcoes, NullLogger<ConsumidorPedidos>.Instance);
        var esperas = new ConcurrentBag<double>();
        metricas.Observador = r => esperas.Add(r.Espera.TotalMilliseconds);

        await consumidor.StartAsync(CancellationToken.None);
        var relogio = Stopwatch.StartNew();
        var amostras = new List<(double T, int Prof, int And, int Banco, long Proc)>();
        using var parar = new CancellationTokenSource();
        var amostrador = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(parar.Token).ConfigureAwait(false))
            {
                lock (amostras)
                {
                    amostras.Add((relogio.Elapsed.TotalMilliseconds, fila.Profundidade, metricas.EmAndamento, limitador.EmUso, metricas.Processados));
                }
            }
        });

        // Produtores: cada pedido é uma "requisição" independente (não esperamos uma para enviar a próxima).
        var tentativas = new List<Task<ResultadoEnfileiramento>>();
        var rnd = new Random(7);
        long enviados = 0;
        while (relogio.Elapsed < Duracao)
        {
            var alvo = (long)(TaxaPorSegundo * relogio.Elapsed.TotalSeconds);
            for (; enviados < alvo; enviados++)
            {
                var pedido = Pedido.Criar($"CLI-{rnd.Next(1000):D4}", Regiao.Sudeste, Prioridade.Normal, [new ItemPedido("SKU-1", 1 + rnd.Next(5), 10m), new ItemPedido("SKU-2", 1, 5m)]);
                tentativas.Add(fila.EnfileirarAsync(pedido, CategoriaProcessamento.Padrao, default).AsTask());
            }

            await Task.Delay(1);
        }

        var resultados = await Task.WhenAll(tentativas);
        var fimOferta = relogio.Elapsed;
        await consumidor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token); // Drenar
        var fimTotal = relogio.Elapsed;
        await parar.CancelAsync();
        try { await amostrador; } catch (OperationCanceledException) { }
        consumidor.Dispose();
        SqliteLimpar(arquivo);

        foreach (var a in amostras)
        {
            linhaTempo.Append(CultureInfo.InvariantCulture, $"{c.Nome[..1]},{a.T:F0},{a.Prof},{a.And},{a.Banco},{a.Proc}\n");
        }

        var ofertados = resultados.Length;
        var espera = esperas.Order().ToArray();
        var banco = tempoBanco.Order().ToArray();
        return string.Join(Environment.NewLine,
            $"ofertados={ofertados} aceitos={metricas.Aceitos} rejeitados={metricas.RejeitadosFilaCheia} descartados={metricas.Descartados} processados={metricas.Processados} falhas={metricas.Falhas} abandonados={metricas.Abandonados}",
            $"throughput={metricas.Processados / fimTotal.TotalSeconds:F0}/s  fim_oferta={fimOferta.TotalSeconds:F1}s  drenado_em={fimTotal.TotalSeconds:F1}s",
            $"profundidade max={amostras.Max(a => a.Prof)} media={amostras.Average(a => a.Prof):F0}  banco_em_uso max={limitador.MaximoObservado}",
            $"espera_na_fila ms p50={P(espera, 50):F1} p95={P(espera, 95):F1} p99={P(espera, 99):F1} max={espera.LastOrDefault():F1}",
            $"operacoes_banco={banco.Length} ({(double)banco.Length / Math.Max(1, metricas.Processados):F2} por pedido)",
            $"operacao_banco ms p50={P(banco, 50):F2} p95={P(banco, 95):F2} p99={P(banco, 99):F2} (n={banco.Length})");
    }

    private static double P(double[] ordenado, double percentil) =>
        ordenado.Length == 0 ? 0 : ordenado[Math.Clamp((int)Math.Ceiling(percentil / 100 * ordenado.Length) - 1, 0, ordenado.Length - 1)];

    private static void SqliteLimpar(string arquivo)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { arquivo, arquivo + "-journal", arquivo + "-wal", arquivo + "-shm" })
        {
            File.Delete(f);
        }
    }

    /// <summary>Por que não "if (!ContainsKey) d[k] = 0; d[k]++": incrementos se perdem.</summary>
    private static void DemonstrarCheckThenAct()
    {
        const int total = 800_000;
        var ingenuo = new ConcurrentDictionary<int, long>();
        var atomico = new ConcurrentDictionary<int, long>();
        Parallel.For(0, total, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var chave = i % 4;
            if (!ingenuo.ContainsKey(chave))
            {
                ingenuo[chave] = 0;
            }

            ingenuo[chave] = ingenuo[chave] + 1;                    // ler-modificar-escrever NÃO atômico
            atomico.AddOrUpdate(chave, 1, static (_, v) => v + 1); // atômico
        });
        Console.WriteLine($"## Check-then-act com {Environment.ProcessorCount} threads, {total} incrementos");
        Console.WriteLine($"ingênuo={ingenuo.Values.Sum()} (perdidos {total - ingenuo.Values.Sum()})  AddOrUpdate={atomico.Values.Sum()}");
        Console.WriteLine();
    }

    private sealed class SeletorExperimento : ISeletorProcessador
    {
        public bool Suporta(CategoriaProcessamento categoria) => true;

        public IProcessadorPedido Obter(CategoriaProcessamento categoria) => new ProcessadorComTrabalhoExterno();
    }

    private sealed class ProcessadorComTrabalhoExterno : IProcessadorPedido
    {
        public async ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken)
        {
            await Task.Delay(TrabalhoExterno, cancellationToken);
            return new ResultadoProcessamento("Experimento", 1, "ok");
        }
    }

    private sealed class RepositorioCronometrado(IRepositorioPedidos interno, ConcurrentBag<double> tempos) : IRepositorioPedidos
    {
        public async Task AdicionarAsync(Pedido pedido, CategoriaProcessamento categoria, CancellationToken ct)
        {
            var inicio = Stopwatch.GetTimestamp();
            await interno.AdicionarAsync(pedido, categoria, ct);
            tempos.Add(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        }

        public async Task AdicionarLoteAsync(IReadOnlyList<(Pedido Pedido, CategoriaProcessamento Categoria)> pedidos, CancellationToken ct)
        {
            var inicio = Stopwatch.GetTimestamp();
            await interno.AdicionarLoteAsync(pedidos, ct);
            tempos.Add(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        }

        public async Task<int> ConcluirLoteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            var inicio = Stopwatch.GetTimestamp();
            var n = await interno.ConcluirLoteAsync(ids, ct);
            tempos.Add(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            return n;
        }

        public async Task<ResultadoAlteracaoStatus> AlterarStatusAsync(Guid id, StatusPedido novo, CancellationToken ct)
        {
            var inicio = Stopwatch.GetTimestamp();
            var r = await interno.AlterarStatusAsync(id, novo, ct);
            tempos.Add(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            return r;
        }
    }
}

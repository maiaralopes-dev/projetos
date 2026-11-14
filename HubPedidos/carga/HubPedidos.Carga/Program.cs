using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;

// Gerador de carga em MODELO ABERTO (taxa de chegada constante).
// Cada requisição é agendada para t0 + i/taxa e disparada sem esperar as anteriores.
// A latência é medida a partir do instante AGENDADO: se o servidor (ou o gerador)
// atrasa, o atraso entra na medida, evitando a "omissão coordenada".
//
// Uso:
//   dotnet run -c Release --project carga/HubPedidos.Carga -- \
//     --url http://localhost:5080 --aquecimento 10 --duracao 30 \
//     --taxa-criacao 100 --taxa-consulta 200 --rotulo antes --saida docs/evidencias/atividade5

var cfg = Config.Ler(args);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
using var http = new HttpClient(new SocketsHttpHandler
{
    MaxConnectionsPerServer = 1024,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
})
{ BaseAddress = new Uri(cfg.Url), Timeout = Timeout.InfiniteTimeSpan };

var amostras = new ConcurrentBag<Amostra>();
var emVoo = 0;
var maxEmVoo = 0;
var naoEnviados = 0;
var acertosCache = 0;
const int LimiteEmVoo = 4_000; // proteção do próprio gerador
var total = TimeSpan.FromSeconds(cfg.Aquecimento + cfg.Duracao);
var relogio = Stopwatch.StartNew();

Console.WriteLine($"Carga: criação {cfg.TaxaCriacao}/s + consulta {cfg.TaxaConsulta}/s | aquecimento {cfg.Aquecimento}s + medição {cfg.Duracao}s | timeout {cfg.TimeoutMs} ms | {cfg.Url}");

var rotas = new List<Task>();
if (cfg.TaxaCriacao > 0)
{
    rotas.Add(AgendarAsync("POST /pedidos", cfg.TaxaCriacao, CriarAsync, semente: 1));
}

if (cfg.TaxaConsulta > 0)
{
    rotas.Add(AgendarAsync("GET " + cfg.UrlConsulta.Split('?')[0], cfg.TaxaConsulta, ConsultarAsync, semente: 2));
}

await Task.WhenAll(rotas);
await AguardarEmVooAsync();

var resumo = Resumir();
Console.WriteLine(resumo);
if (cfg.Saida is { } dir)
{
    Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir, $"carga-{cfg.Rotulo}.md"), resumo);
    var csv = new StringBuilder("rota,fase,agendado_ms,latencia_ms,status,erro\n");
    foreach (var a in amostras.OrderBy(a => a.AgendadoMs))
    {
        csv.Append(CultureInfo.InvariantCulture, $"{a.Rota},{a.Fase},{a.AgendadoMs:F1},{a.LatenciaMs:F2},{a.Status},{a.Erro}\n");
    }

    await File.WriteAllTextAsync(Path.Combine(dir, $"carga-{cfg.Rotulo}.csv"), csv.ToString());
}

return;

async Task AgendarAsync(string rota, double taxa, Func<Random, CancellationToken, Task<int>> enviar, int semente)
{
    var rnd = new Random(semente);
    var intervalo = 1000.0 / taxa;
    for (long i = 0; ; i++)
    {
        var agendado = i * intervalo;
        if (agendado >= total.TotalMilliseconds)
        {
            break;
        }

        var falta = agendado - relogio.Elapsed.TotalMilliseconds;
        if (falta > 1)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(falta));
        }


        var fase = agendado < cfg.Aquecimento * 1000 ? "aquecimento" : "medicao";
        if (Volatile.Read(ref emVoo) >= LimiteEmVoo)
        {
            Interlocked.Increment(ref naoEnviados);
            amostras.Add(new(rota, fase, agendado, double.NaN, 0, "nao_enviado"));
            continue;
        }

        var semente2 = rnd.Next();
        _ = DispararAsync(rota, fase, agendado, ct => enviar(new Random(semente2), ct));
    }
}

async Task DispararAsync(string rota, string fase, double agendado, Func<CancellationToken, Task<int>> enviar)
{
    var atual = Interlocked.Increment(ref emVoo);
    int max;
    while (atual > (max = Volatile.Read(ref maxEmVoo)) && Interlocked.CompareExchange(ref maxEmVoo, atual, max) != max)
    {
    }
    using var cts = new CancellationTokenSource(cfg.TimeoutMs);
    int status = 0;
    string erro = "";
    try
    {
        status = await enviar(cts.Token);
        if (status >= 400)
        {
            erro = "http_" + status;
        }

    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { erro = "timeout"; }
    catch (HttpRequestException ex) { erro = "conexao:" + (ex.HttpRequestError.ToString()); }
    finally
    {
        // Latência desde o instante AGENDADO (inclui atraso de fila no servidor e no cliente).
        amostras.Add(new(rota, fase, agendado, relogio.Elapsed.TotalMilliseconds - agendado, status, erro));
        Interlocked.Decrement(ref emVoo);
    }
}

async Task<int> CriarAsync(Random rnd, CancellationToken ct)
{
    var itens = Enumerable.Range(0, rnd.Next(1, 4))
        .Select(i => new { sku = $"SKU-{rnd.Next(1, 400):D4}-{i}", quantidade = rnd.Next(1, 10), precoUnitario = rnd.Next(100, 50_000) / 100m })
        .ToArray();
    var corpo = new { clienteId = $"CLI-{rnd.Next(1, 2_000):D5}", regiao = "Sudeste", prioridade = "Normal", itens };
    using var resposta = await http.PostAsJsonAsync("/pedidos", corpo, ct);
    await resposta.Content.LoadIntoBufferAsync(ct);
    return (int)resposta.StatusCode;
}

async Task<int> ConsultarAsync(Random rnd, CancellationToken ct)
{
    using var resposta = await http.GetAsync(cfg.UrlConsulta.Replace("{cliente}", $"CLI-{rnd.Next(1, 2_000):D5}", StringComparison.Ordinal), ct);
    await resposta.Content.LoadIntoBufferAsync(ct);
    if (resposta.Headers.Age.HasValue)
    {
        Interlocked.Increment(ref acertosCache); // resposta servida pelo output cache
    }
    return (int)resposta.StatusCode;
}

async Task AguardarEmVooAsync()
{
    var limite = Stopwatch.StartNew();
    while (Volatile.Read(ref emVoo) > 0 && limite.Elapsed < TimeSpan.FromMilliseconds(cfg.TimeoutMs + 2_000))
    {
        await Task.Delay(20);
    }
}

string Resumir()
{
    var sb = new StringBuilder();
    var c = CultureInfo.InvariantCulture;
    sb.AppendLine(c, $"## Carga '{cfg.Rotulo}' — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
    sb.AppendLine();
    sb.AppendLine(c, $"Modelo aberto; criação {cfg.TaxaCriacao}/s + consulta {cfg.TaxaConsulta}/s; aquecimento {cfg.Aquecimento}s (descartado); medição {cfg.Duracao}s; timeout {cfg.TimeoutMs} ms; máx. em voo {maxEmVoo}; não enviados (gerador saturado) {naoEnviados}.");
    sb.AppendLine(c, $"Consulta: {cfg.UrlConsulta}; respostas com cabeçalho Age (vindas do output cache): {acertosCache}.");
    sb.AppendLine("Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).");
    sb.AppendLine();
    sb.AppendLine("| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |");
    sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (var g in amostras.Where(a => a.Fase == "medicao").GroupBy(a => a.Rota).OrderBy(g => g.Key))
    {
        var lat = g.Select(a => double.IsNaN(a.LatenciaMs) ? cfg.TimeoutMs : Math.Min(a.LatenciaMs, cfg.TimeoutMs)).Order().ToArray();
        var ok = g.Count(a => a.Status is >= 200 and < 300 && a.Erro == "");
        var http = g.Count(a => a.Erro.StartsWith("http_", StringComparison.Ordinal));
        var to = g.Count(a => a.Erro == "timeout");
        var outros = g.Count() - ok - http - to;
        sb.AppendLine(c, $"| {g.Key} | {g.Count()} | {ok} | {http} | {to} | {outros} | {ok / cfg.Duracao:F1} | {P(lat, 50):F1} | {P(lat, 95):F1} | {P(lat, 99):F1} | {lat[^1]:F1} |");
        var tipos = g.Where(a => a.Erro != "").GroupBy(a => a.Erro.Split(':')[0] + (a.Erro.Contains(':', StringComparison.Ordinal) ? ":" + a.Erro.Split(':')[1] : "")).Select(t => $"{t.Key}={t.Count()}");
        if (tipos.Any())
        {
            sb.AppendLine(c, $"|  ↳ erros: {string.Join(", ", tipos)} |||||||||||");
        }
    }

    return sb.ToString();
}

static double P(double[] ordenado, double p) =>
    ordenado.Length == 0 ? double.NaN : ordenado[Math.Clamp((int)Math.Ceiling(p / 100 * ordenado.Length) - 1, 0, ordenado.Length - 1)];

internal sealed record Amostra(string Rota, string Fase, double AgendadoMs, double LatenciaMs, int Status, string Erro);

internal sealed record Config(string Url, int Aquecimento, int Duracao, double TaxaCriacao, double TaxaConsulta, int TimeoutMs, string Rotulo, string? Saida, string UrlConsulta)
{
    public static Config Ler(string[] a)
    {
        string V(string nome, string padrao)
        {
            var i = Array.IndexOf(a, "--" + nome);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : padrao;
        }

        return new(V("url", "http://localhost:5080"), int.Parse(V("aquecimento", "10"), CultureInfo.InvariantCulture), int.Parse(V("duracao", "30"), CultureInfo.InvariantCulture),
            double.Parse(V("taxa-criacao", "100"), CultureInfo.InvariantCulture), double.Parse(V("taxa-consulta", "200"), CultureInfo.InvariantCulture),
            int.Parse(V("timeout", "5000"), CultureInfo.InvariantCulture), V("rotulo", "execucao"), a.Contains("--saida") ? V("saida", "") : null,
            V("url-consulta", "/pedidos?clienteId={cliente}&tamanho=20"));
    }
}

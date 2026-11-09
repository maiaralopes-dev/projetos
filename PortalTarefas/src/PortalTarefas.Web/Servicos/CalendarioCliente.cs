using System.Diagnostics;
using Polly.CircuitBreaker;
using Polly.Timeout;
using PortalTarefas.Web.Observabilidade;

namespace PortalTarefas.Web.Servicos;

public sealed record Feriado(DateOnly Data, string Nome);

/// <summary>Resultado sem exceção, para o endpoint decidir o status HTTP.</summary>
public sealed record ResultadoCalendario(IReadOnlyList<Feriado>? Feriados, string Resultado);

/// <summary>
/// Cliente tipado da dependência externa "serviço de calendário". O HttpClient recebe, por configuração,
/// timeout por tentativa, retry só para falhas transitórias em GET e circuit breaker (ver Program.cs).
/// </summary>
public sealed class CalendarioCliente(HttpClient http, Telemetria telemetria, ILogger<CalendarioCliente> logger)
{
    public async Task<ResultadoCalendario> ObterFeriadosAsync(int ano, CancellationToken ct)
    {
        var inicio = Stopwatch.GetTimestamp();
        var resultado = "falha";
        try
        {
            var feriados = await http.GetFromJsonAsync<List<Feriado>>($"feriados/{ano}", ct) ?? [];
            resultado = "sucesso";
            return new(feriados, resultado);
        }
        catch (BrokenCircuitException)
        {
            resultado = "circuito_aberto";
            logger.LogWarning("Calendário: circuito aberto; chamada recusada sem acessar a dependência");
        }
        catch (TimeoutRejectedException)
        {
            resultado = "timeout";
            logger.LogWarning("Calendário: tempo limite total excedido");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Calendário: falha {Motivo} após as tentativas configuradas",
                ex.StatusCode is { } s ? $"HTTP {(int)s}" : ex.HttpRequestError.ToString());
        }
        finally
        {
            var ms = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            telemetria.ChamadaCalendario(ms, resultado);
            logger.LogInformation("Calendário: ano {Ano} -> {Resultado} em {DuracaoMs:F0} ms", ano, resultado, ms);
        }
        return new(null, resultado);
    }
}

/// <summary>Propaga o identificador de correlação para a dependência (o traceparent W3C é propagado pelo OpenTelemetry).</summary>
public sealed class PropagarCorrelacaoHandler(IHttpContextAccessor acessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (acessor.HttpContext?.Items[Infraestrutura.CorrelationIdMiddleware.ChaveItem] is string id)
            request.Headers.TryAddWithoutValidation(Infraestrutura.CorrelationIdMiddleware.NomeCabecalho, id);
        return base.SendAsync(request, ct);
    }
}

/// <summary>Readiness: a dependência essencial responde? Usa um cliente sem retry, com timeout curto.</summary>
public sealed class CalendarioHealthCheck(IHttpClientFactory fabrica) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var resp = await fabrica.CreateClient("calendario-saude").GetAsync("health", ct);
            return resp.IsSuccessStatusCode
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Serviço de calendário respondeu.")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"Serviço de calendário respondeu {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Serviço de calendário inacessível.");
        }
    }
}

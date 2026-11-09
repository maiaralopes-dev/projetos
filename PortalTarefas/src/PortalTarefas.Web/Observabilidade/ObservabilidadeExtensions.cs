using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PortalTarefas.Web.Observabilidade;

public static class ObservabilidadeExtensions
{
    /// <summary>
    /// Configura OpenTelemetry (traces, métricas e logs). Exportadores:
    ///  - console, quando Telemetria:Console = true (evidência local);
    ///  - OTLP, quando OTEL_EXPORTER_OTLP_ENDPOINT estiver definido (coletor/painel local, ex.: Aspire Dashboard).
    /// </summary>
    public static WebApplicationBuilder AddObservabilidade(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<Telemetria>();
        var console = builder.Configuration.GetValue<bool>("Telemetria:Console");
        var otlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("portal-tarefas-api",
                serviceVersion: builder.Configuration["APP_VERSION"] ?? "dev",
                serviceInstanceId: Environment.MachineName))
            .WithTracing(t =>
            {
                t.AddSource(Telemetria.Nome)
                 .AddAspNetCoreInstrumentation(o =>
                     // Sondas de saúde e ping geram ruído e não ajudam no diagnóstico.
                     o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/ping"))
                 .AddHttpClientInstrumentation()
                 .AddSqlClientInstrumentation();
                if (console) t.AddConsoleExporter();
            })
            .WithMetrics(m =>
            {
                m.AddMeter(Telemetria.Nome)
                 .AddAspNetCoreInstrumentation()           // http.server.request.duration etc.
                 .AddHttpClientInstrumentation()
                 .AddMeter("Microsoft.AspNetCore.RateLimiting", "Microsoft.AspNetCore.Diagnostics", "Polly");
                if (console) m.AddConsoleExporter((_, r) => r.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15000);
            })
            .WithLogging(_ => { }, o => { o.IncludeScopes = true; o.IncludeFormattedMessage = true; });

        if (otlp) otel.UseOtlpExporter();   // traces + métricas + logs para o mesmo endpoint OTLP
        return builder;
    }
}

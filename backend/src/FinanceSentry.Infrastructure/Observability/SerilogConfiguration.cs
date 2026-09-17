namespace FinanceSentry.Infrastructure.Observability;

using FinanceSentry.Core.Observability;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

/// <summary>
/// Central Serilog setup (FR-003/011). Console output is one compact JSON object per line — the
/// platform contract's "JSON logs with a trace id on stdout" (guardrail 1, spec 048): the box log
/// agent reads stdout, so the rendered message (<c>@m</c>) plus <c>TraceId</c>/<c>SpanId</c> from
/// <see cref="TraceEnricher"/> is what every downstream reader sees. Keeps the rolling-file sink,
/// suppresses the EF Core <c>Database.Command</c> SQL flood that made grep-based triage slow (FR-011,
/// override-able via config), enriches every event with a bounded <c>module</c> + <c>app</c>, and —
/// when a Loki URL is configured — ships structured logs to Loki via the batched sink (fire-and-forget:
/// shipping failures are swallowed by the sink and never affect request handling, FR-003). The Loki
/// sink stays until the box log agent lands (captain, 2026-09-16).
/// </summary>
public static class SerilogConfiguration
{
    private const string AppName = "finance-sentry";
    private const string LokiUrlConfigKey = "Observability:Loki:Url";

    // FR-004 (feature 024): cap the rolling file so a single day's logs cannot exhaust host disk.
    private const long FileSizeLimitBytes = 100L * 1024 * 1024;
    private const int RetainedFileCountLimit = 14;

    /// <summary>Matches the <c>UseSerilog</c> host callback signature; tags events <c>app=finance-sentry</c>.</summary>
    public static void Configure(HostBuilderContext context, LoggerConfiguration loggerConfiguration)
        => Configure(context, loggerConfiguration, AppName);

    /// <summary>
    /// The same setup for another host of this product (the MCP server), tagged with its own
    /// <c>app</c> so the shared log stream tells the processes apart.
    /// </summary>
    public static Action<HostBuilderContext, LoggerConfiguration> For(string appName)
        => (context, loggerConfiguration) => Configure(context, loggerConfiguration, appName);

    private static void Configure(HostBuilderContext context, LoggerConfiguration loggerConfiguration, string appName)
    {
        var configuration = context.Configuration;

        loggerConfiguration
            .ReadFrom.Configuration(configuration)
            // EF SQL is the noise that slowed incident triage — keep it at Warning by default; a
            // Serilog:MinimumLevel:Override in config can raise it back to Debug without a code change.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With<ModuleEnricher>()
            .Enrich.With<TraceEnricher>()
            .Enrich.WithProperty("app", appName)
            .WriteTo.Console(new RenderedCompactJsonFormatter())
            .WriteTo.File(
                "logs/app-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCountLimit,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true);

        var lokiUrl = configuration[LokiUrlConfigKey];
        if (!string.IsNullOrWhiteSpace(lokiUrl))
        {
            // JSON payload so structured properties (CorrelationId, SourceContext, …) are searchable in
            // Loki via `| json` (SC-002 correlation-id grouping); only app/module/level become labels so
            // cardinality stays bounded (FR-007).
            loggerConfiguration.WriteTo.GrafanaLoki(
                lokiUrl,
                textFormatter: new LokiJsonTextFormatter(),
                labels: [new LokiLabel { Key = "app", Value = appName }],
                propertiesAsLabels: ["module", "level"]);
        }
    }
}

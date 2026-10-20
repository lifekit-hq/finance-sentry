namespace FinanceSentry.Infrastructure.Observability;

using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

/// <summary>
/// Central Serilog setup (FR-003/011). Console output is one compact JSON object per line — the
/// platform contract's "JSON logs with a trace id on stdout" (guardrail 1, spec 048): the box log
/// agent reads stdout and ships it to Loki, so the rendered message (<c>@m</c>) plus the W3C trace and
/// span ids Serilog 4 captures from <see cref="System.Diagnostics.Activity.Current"/> (<c>@tr</c>/<c>@sp</c>)
/// is what every downstream reader sees. The api writes no log sink of its own (no Loki, no file).
/// Suppresses the EF Core <c>Database.Command</c> SQL flood that made grep-based triage slow (FR-011,
/// override-able via config) and enriches every event with a bounded <c>module</c> + <c>app</c>.
/// </summary>
public static class SerilogConfiguration
{
    private const string AppName = "finance-sentry";

    /// <summary>Matches the <c>UseSerilog</c> host callback signature; tags events <c>app=finance-sentry</c>.</summary>
    public static void Configure(HostBuilderContext context, LoggerConfiguration loggerConfiguration)
        => Configure(context, loggerConfiguration, AppName);

    /// <summary>
    /// The same setup for another host of this product (the MCP server), tagged with its own
    /// <c>app</c> so the shared log stream tells the processes apart.
    /// </summary>
    public static Action<HostBuilderContext, LoggerConfiguration> For(string appName)
        => (context, loggerConfiguration) => Configure(context, loggerConfiguration, appName);

    private static void Configure(
        HostBuilderContext context,
        LoggerConfiguration loggerConfiguration,
        string appName)
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
            .Enrich.WithProperty("app", appName)
            .WriteTo.Console(new RenderedCompactJsonFormatter());
    }
}

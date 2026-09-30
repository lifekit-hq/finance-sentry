using FinanceSentry.Infrastructure.Observability.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace FinanceSentry.Mcp;

/// <summary>
/// The MCP HTTP host's platform-contract surface (guardrail 1, spec 048): liveness, readiness and
/// Prometheus metrics, mapped <see cref="Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute"/> so
/// the platform probes them without a token. Readiness is the api's shape
/// (<see cref="ReadinessResponseWriter"/>) over the checks tagged <c>ready</c> — today the Postgres
/// connection every tool needs. Metrics are ASP.NET Core + runtime instrumentation under the resource
/// <c>finance-sentry-mcp</c>; not the api's <c>AddObservabilityMetrics</c>, which carries the Hangfire
/// job meter and the OTLP trace spine this host does not have.
/// </summary>
public static class McpPlatformEndpoints
{
    public const string ServiceName = "finance-sentry-mcp";
    public const string HealthPath = "/health";
    public const string ReadyPath = "/ready";
    public const string MetricsPath = "/metrics";

    private const string ReadyTag = "ready";
    private const string DatabaseCheckName = "database";

    public static IServiceCollection AddMcpPlatformEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."),
                name: DatabaseCheckName,
                tags: [ReadyTag]);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        return services;
    }

    public static WebApplication MapMcpPlatformEndpoints(this WebApplication app)
    {
        app.MapGet(HealthPath, () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .AllowAnonymous();

        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = ReadinessResponseWriter.WriteAsync,
        }).AllowAnonymous();

        app.MapPrometheusScrapingEndpoint(MetricsPath).AllowAnonymous();

        return app;
    }
}

namespace FinanceSentry.Infrastructure.Observability;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// Wires OpenTelemetry metrics (FR-001/002): ASP.NET Core request instrumentation, .NET runtime
/// instrumentation, the custom job <see cref="JobMetrics"/> meter, and the Prometheus exporter served
/// at <c>/metrics</c>. HTTP labels use route templates (not raw URLs) so cardinality stays bounded
/// (FR-007); the endpoint is intended to be reachable only over the compose network / Tailscale (FR-006).
/// </summary>
public static class OpenTelemetryConfiguration
{
    private const string ServiceName = "finance-sentry";
    private const string OtlpEndpointConfigKey = "Observability:Otlp:Endpoint";
    private const string DefaultOtlpEndpoint = "http://otel-collector:4318";

    public static IServiceCollection AddObservabilityMetrics(this IServiceCollection services)
    {
        // Shared singleton: the Hangfire JobMetricsFilter and the meter provider observe the same instruments.
        services.AddSingleton<JobMetrics>();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(JobMetrics.MeterName)
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Wires the HTTP trace spine (spec 023 amendment, 2026-09-13): ASP.NET Core + HttpClient +
    /// Npgsql spans, exported via OTLP/HTTP. <c>AddOpenTelemetry()</c> is safe to call again here —
    /// it composes onto the same provider registrations <see cref="AddObservabilityMetrics"/> already
    /// added to the DI container rather than starting a second one, so the resource configured there
    /// still applies to traces. The endpoint mirrors <c>Observability:Loki:Url</c>'s shape: unset falls
    /// back to the in-network collector default, an explicit empty value disables the exporter (dev).
    /// </summary>
    public static IServiceCollection AddObservabilityTracing(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration[OtlpEndpointConfigKey] ?? DefaultOtlpEndpoint;

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }

    /// <summary>Serves Prometheus exposition at <c>/metrics</c> (the exporter's default path).</summary>
    public static IEndpointRouteBuilder MapObservabilityMetricsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPrometheusScrapingEndpoint();
        return endpoints;
    }
}

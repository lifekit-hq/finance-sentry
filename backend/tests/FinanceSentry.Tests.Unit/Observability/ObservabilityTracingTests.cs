namespace FinanceSentry.Tests.Unit.Observability;

using System.Diagnostics;
using FinanceSentry.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

/// <summary>
/// Unit tests for the trace pipeline built by <see cref="OpenTelemetryConfiguration.AddObservabilityTracing"/>:
/// Npgsql spans are exported only when they belong to a parent trace.
/// </summary>
public class ObservabilityTracingTests
{
    private static readonly ActivitySource NpgsqlSource = new(ParentlessNpgsqlSpanFilter.NpgsqlSourceName);
    private static readonly ActivitySource RequestSource = new("FinanceSentry.Tests.ObservabilityTracing");

    [Fact]
    public void ParentlessNpgsqlSpan_IsNotExported()
    {
        var exported = new List<Activity>();
        using var provider = BuildProvider(exported);
        Activity.Current = null;

        using (NpgsqlSource.StartActivity("parentless-command", ActivityKind.Client))
        {
        }

        exported.Should().NotContain(a => a.DisplayName == "parentless-command");
    }

    [Fact]
    public void NpgsqlSpanUnderParent_IsExported()
    {
        var exported = new List<Activity>();
        using var provider = BuildProvider(exported);
        Activity.Current = null;

        using (RequestSource.StartActivity("request"))
        using (NpgsqlSource.StartActivity("child-command", ActivityKind.Client))
        {
        }

        exported.Should().Contain(a => a.DisplayName == "child-command");
        exported.Should().Contain(a => a.DisplayName == "request");
    }

    private static TracerProvider BuildProvider(List<Activity> exported)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Observability:Otlp:Endpoint"] = string.Empty })
            .Build();

        return Sdk.CreateTracerProviderBuilder()
            .AddObservabilityTracing(configuration)
            .AddSource(RequestSource.Name)
            .AddProcessor(new SimpleActivityExportProcessor(new CollectingExporter(exported)))
            .Build();
    }

    private sealed class CollectingExporter(List<Activity> exported) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (exported)
            {
                foreach (var activity in batch)
                {
                    exported.Add(activity);
                }
            }

            return ExportResult.Success;
        }
    }
}

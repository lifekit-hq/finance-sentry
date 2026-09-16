namespace FinanceSentry.Infrastructure.Observability;

using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Adds <c>TraceId</c>/<c>SpanId</c> from <see cref="Activity.Current"/> so log lines correlate with
/// OpenTelemetry traces (spec 023 amendment, 2026-09-13). Serilog 3.1.1 does not read Activity trace
/// ids natively — this enricher is the one-increment path instead of bumping to Serilog 4.x.
/// Both are structured JSON properties only, NEVER Loki labels: TraceId/SpanId are per-request and
/// promoting either to a label would make Loki's label index cardinality-explode (see
/// <see cref="ModuleEnricher"/> for the same house rule applied to <c>module</c>).
/// </summary>
public sealed class TraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
    }
}

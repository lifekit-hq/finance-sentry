namespace FinanceSentry.Tests.Unit.Observability;

using System.Diagnostics;
using FinanceSentry.Infrastructure.Observability;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Xunit;

/// <summary>
/// Unit tests for <see cref="TraceEnricher"/> (spec 023 amendment, 2026-09-13): TraceId/SpanId are
/// present when an <see cref="Activity"/> is current and absent otherwise, so log correlation degrades
/// gracefully outside a traced request instead of emitting empty/garbage ids.
/// </summary>
public class TraceEnricherTests
{
    private const string SourceName = "FinanceSentry.Tests.TraceEnricher";
    private static readonly ActivitySource ActivitySource = new(SourceName);

    [Fact]
    public void Enrich_UnderActivity_AddsTraceIdAndSpanId()
    {
        using var listener = StartListener();
        using var activity = ActivitySource.StartActivity("test-activity");
        activity.Should().NotBeNull();

        var logEvent = CaptureEnrichedEvent();

        logEvent.Properties.Should().ContainKey("TraceId");
        logEvent.Properties.Should().ContainKey("SpanId");
        ((ScalarValue)logEvent.Properties["TraceId"]).Value.Should().Be(activity!.TraceId.ToHexString());
        ((ScalarValue)logEvent.Properties["SpanId"]).Value.Should().Be(activity.SpanId.ToHexString());
    }

    [Fact]
    public void Enrich_WithoutActivity_OmitsTraceIdAndSpanId()
    {
        Activity.Current = null;

        var logEvent = CaptureEnrichedEvent();

        logEvent.Properties.Should().NotContainKey("TraceId");
        logEvent.Properties.Should().NotContainKey("SpanId");
    }

    private static LogEvent CaptureEnrichedEvent()
    {
        LogEvent? captured = null;
        var logger = new LoggerConfiguration()
            .Enrich.With<TraceEnricher>()
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logger.Information("probe");

        captured.Should().NotBeNull();
        return captured!;
    }

    private static ActivityListener StartListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class DelegatingSink(Action<LogEvent> onEmit) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => onEmit(logEvent);
    }
}

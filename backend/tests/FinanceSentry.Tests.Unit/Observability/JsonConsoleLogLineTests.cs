namespace FinanceSentry.Tests.Unit.Observability;

using System.Diagnostics;
using System.Text.Json;
using FinanceSentry.Core.Observability;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Xunit;

/// <summary>
/// The console line shape the platform contract checks (guardrail 1, spec 048): one JSON object per
/// line, with the rendered message and — inside a traced request — a <c>TraceId</c> key. Exercises
/// the formatter + enricher pair <see cref="FinanceSentry.Infrastructure.Observability.SerilogConfiguration"/>
/// writes to the console, through a <see cref="TextWriter"/> so the test never touches the process
/// stdout.
/// </summary>
public class JsonConsoleLogLineTests
{
    private const string SourceName = "FinanceSentry.Tests.JsonConsoleLogLine";
    private static readonly ActivitySource ActivitySource = new(SourceName);

    [Fact]
    public void UnderActivity_LineIsOneJsonObjectWithRenderedMessageAndTraceId()
    {
        using var listener = StartListener();
        using var activity = ActivitySource.StartActivity("request");
        activity.Should().NotBeNull();

        var line = WriteOneLine("hello {Who}", "contract");

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        root.GetProperty("@t").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("@m").GetString().Should().Be("hello \"contract\"");
        root.GetProperty("Who").GetString().Should().Be("contract");
        root.GetProperty("TraceId").GetString().Should().Be(activity!.TraceId.ToHexString());
        root.GetProperty("SpanId").GetString().Should().Be(activity.SpanId.ToHexString());
    }

    [Fact]
    public void WithoutActivity_LineIsJsonWithoutTraceId()
    {
        Activity.Current = null;

        var line = WriteOneLine("plain", null);

        using var doc = JsonDocument.Parse(line);
        doc.RootElement.TryGetProperty("TraceId", out _).Should().BeFalse();
        doc.RootElement.GetProperty("@m").GetString().Should().Be("plain");
    }

    private static string WriteOneLine(string template, string? argument)
    {
        var output = new StringWriter();
        var logger = new LoggerConfiguration()
            .Enrich.With<TraceEnricher>()
            .WriteTo.Sink(new FormattingSink(new RenderedCompactJsonFormatter(), output))
            .CreateLogger();

        if (argument is null)
            logger.Information(template);
        else
            logger.Information(template, argument);
        logger.Dispose();

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        return lines[0];
    }

    /// <summary>Runs the console formatter against a captured writer instead of the process stdout.</summary>
    private sealed class FormattingSink(ITextFormatter formatter, TextWriter output) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => formatter.Format(logEvent, output);
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
}

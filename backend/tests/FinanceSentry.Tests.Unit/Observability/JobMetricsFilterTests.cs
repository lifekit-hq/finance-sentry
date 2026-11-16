namespace FinanceSentry.Tests.Unit.Observability;

using FinanceSentry.Infrastructure.Observability;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>A job that fails for good is named in the log together with its exception, not only counted in the metric.</summary>
public class JobMetricsFilterTests
{
    [Fact]
    public void TerminalFailure_LogsTheJobNameAndTheException()
    {
        using var metrics = new JobMetrics();
        var log = new CapturingLogger();
        var filter = new JobMetricsFilter(metrics, log);
        var error = new InvalidOperationException("Theme attention could not read 9 of 11 phrase(s)");

        filter.LogTerminalFailure("ThemeAttentionRefreshJob.ExecuteAsync", "564548", error);

        var entry = log.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("ThemeAttentionRefreshJob.ExecuteAsync").And.Contain("564548");
        entry.Exception.Should().BeSameAs(error);
    }

    [Fact]
    public void TerminalFailure_WithoutALogger_StillRuns()
    {
        using var metrics = new JobMetrics();

        var act = () => new JobMetricsFilter(metrics).LogTerminalFailure("Job.Run", null, null);

        act.Should().NotThrow();
    }

    private sealed class CapturingLogger : ILogger<JobMetricsFilter>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}

namespace FinanceSentry.Tests.Unit.Observability;

using System.Diagnostics.Metrics;
using FinanceSentry.Infrastructure.Observability;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FluentAssertions;
using global::Hangfire;
using global::Hangfire.Common;
using global::Hangfire.States;
using global::Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// A job that fails for good is named in the log together with its exception, and counted in the failure metric,
/// when Hangfire applies its <c>FailedState</c>; a success is counted and not logged as a failure.
/// </summary>
public class JobMetricsFilterTests
{
    private const string FailedCounter = "finance_jobs_failed";
    private const string SucceededCounter = "finance_jobs_succeeded";
    private const string ExpectedJobName = "SampleJob.ExecuteAsync";
    private const string JobId = "564548";

    [Fact]
    public void FailedState_LogsTheJobNameAndTheException_AndCountsTheFailure()
    {
        using var metrics = new JobMetrics();
        var log = new CapturingLogger();
        var filter = new JobMetricsFilter(metrics, log);
        var error = new InvalidOperationException("Theme attention could not read 9 of 11 phrase(s)");
        using var counts = new CounterCapture(FailedCounter, SucceededCounter);

        filter.OnStateApplied(Context(new FailedState(error)), Mock.Of<IWriteOnlyTransaction>());

        var entry = log.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain(ExpectedJobName).And.Contain(JobId);
        entry.Exception.Should().BeSameAs(error);
        counts.Totals.Should().BeEquivalentTo(new Dictionary<string, long> { [$"{FailedCounter}|{ExpectedJobName}"] = 1 });
    }

    [Fact]
    public void FailedState_WithoutALogger_StillCountsTheFailure()
    {
        using var metrics = new JobMetrics();
        using var counts = new CounterCapture(FailedCounter, SucceededCounter);

        var act = () => new JobMetricsFilter(metrics).OnStateApplied(
            Context(new FailedState(new InvalidOperationException("boom"))), Mock.Of<IWriteOnlyTransaction>());

        act.Should().NotThrow();
        counts.Totals.Should().BeEquivalentTo(new Dictionary<string, long> { [$"{FailedCounter}|{ExpectedJobName}"] = 1 });
    }

    [Fact]
    public void SucceededState_CountsTheSuccess_AndLogsNoFailure()
    {
        using var metrics = new JobMetrics();
        var log = new CapturingLogger();
        var filter = new JobMetricsFilter(metrics, log);
        using var counts = new CounterCapture(FailedCounter, SucceededCounter);

        filter.OnStateApplied(Context(new SucceededState(null, 10, 5)), Mock.Of<IWriteOnlyTransaction>());

        log.Entries.Should().BeEmpty();
        counts.Totals.Should().BeEquivalentTo(new Dictionary<string, long> { [$"{SucceededCounter}|{ExpectedJobName}"] = 1 });
    }

    private static ApplyStateContext Context(IState state)
    {
        var job = new Job(typeof(SampleJob), typeof(SampleJob).GetMethod(nameof(SampleJob.ExecuteAsync))!);
        var backgroundJob = new BackgroundJob(JobId, job, DateTime.UtcNow);
        return new ApplyStateContext(
            Mock.Of<JobStorage>(), Mock.Of<IStorageConnection>(), Mock.Of<IWriteOnlyTransaction>(), backgroundJob, state, ProcessingState.StateName);
    }

    public sealed class SampleJob
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    private sealed class CounterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public CounterCapture(params string[] instruments)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == JobMetrics.MeterName && instruments.Contains(instrument.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var job = tags.ToArray().Single(t => t.Key == "job").Value;
                var key = $"{instrument.Name}|{job}";
                Totals[key] = Totals.GetValueOrDefault(key) + value;
            });
            _listener.Start();
        }

        public Dictionary<string, long> Totals { get; } = [];

        public void Dispose() => _listener.Dispose();
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

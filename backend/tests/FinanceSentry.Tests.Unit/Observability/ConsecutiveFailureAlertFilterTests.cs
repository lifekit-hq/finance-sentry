namespace FinanceSentry.Tests.Unit.Observability;

using FinanceSentry.Core.Interfaces;
using System.Diagnostics.Metrics;
using FinanceSentry.Infrastructure.Observability;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for the consecutive-failure streak logic (T029, contracts §4): one alert on the Nth
/// terminal failure, none before it, reset-on-success then re-alert, transient errors excluded, and
/// fire-and-forget dispatch that never throws into the job.
/// </summary>
public class ConsecutiveFailureAlertFilterTests : IDisposable
{
    private const int Threshold = 3;
    private const string Job = "SyncScheduler.ScheduleAllActiveAccounts";

    private readonly Mock<IAlertGeneratorService> _generator = new();
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly InMemoryStreakStore _store = new();
    private readonly JobMetrics _metrics = new();

    public ConsecutiveFailureAlertFilterTests()
    {
        _banking
            .Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[Guid.NewGuid()]);
    }

    public void Dispose() => _metrics.Dispose();

    [Fact]
    public void NthConsecutiveFailure_RaisesExactlyOneAlertWithCount()
    {
        var filter = Build();

        Fail(filter, times: Threshold);

        _generator.Verify(g => g.GenerateJobFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), Job, Threshold, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Further failures in the same streak must not re-alert.
        Fail(filter, times: 2);
        _generator.Verify(g => g.GenerateJobFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void FailuresBelowThreshold_RaiseNoAlert()
    {
        var filter = Build();

        Fail(filter, times: Threshold - 1);

        _generator.VerifyNoOtherCalls();
    }

    [Fact]
    public void SuccessResetsStreak_ThenNextStreakReAlerts()
    {
        var filter = Build();

        Fail(filter, times: Threshold);
        filter.RecordOutcome(Job, succeeded: true, error: null);
        Fail(filter, times: Threshold);

        _generator.Verify(g => g.GenerateJobFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), Job, Threshold, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public void TransientFailures_DoNotIncrementStreak()
    {
        var filter = Build();

        for (var i = 0; i < Threshold + 2; i++)
            filter.RecordOutcome(Job, succeeded: false, error: new TimeoutException());

        _generator.VerifyNoOtherCalls();
        _store.Get(Job).Count.Should().Be(0);
    }

    [Fact]
    public void GeneratorThrow_DoesNotPropagate_AndLeavesStreakOpenToRetry()
    {
        _generator
            .Setup(g => g.GenerateJobFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("alert store down"));
        var filter = Build();

        var act = () => Fail(filter, times: Threshold);

        act.Should().NotThrow();
        // Dispatch failed, so the streak stays un-alerted and the next failure re-attempts.
        _store.Get(Job).Alerted.Should().BeFalse();
        filter.RecordOutcome(Job, succeeded: false, error: new Exception("boom"));
        _generator.Verify(g => g.GenerateJobFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public void SuccessAfterAlertedStreak_ResolvesAlert()
    {
        var filter = Build();

        Fail(filter, times: Threshold);
        filter.RecordOutcome(Job, succeeded: true, error: null);

        _generator.Verify(g => g.ResolveJobFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void SuccessWithoutPriorAlert_DoesNotResolve()
    {
        var filter = Build();

        Fail(filter, times: Threshold - 1);
        filter.RecordOutcome(Job, succeeded: true, error: null);

        _generator.Verify(g => g.ResolveJobFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Gauge_IncrementsAcrossConsecutiveFailures_AndResetsOnSuccess_PerJob()
    {
        const string otherJob = "IBKRSyncJob.Run";
        var filter = Build();

        Fail(filter, times: 2);
        filter.RecordOutcome(otherJob, succeeded: false, error: new Exception("boom"));

        ConsecutiveFailures().Should().BeEquivalentTo(
            new Dictionary<string, int> { [Job] = 2, [otherJob] = 1 });

        filter.RecordOutcome(Job, succeeded: true, error: null);

        ConsecutiveFailures().Should().BeEquivalentTo(
            new Dictionary<string, int> { [Job] = 0, [otherJob] = 1 });

        Fail(filter, times: 1);
        ConsecutiveFailures()[Job].Should().Be(1);
    }

    [Fact]
    public void Gauge_IgnoresTransientFailures()
    {
        var filter = Build();

        Fail(filter, times: 2);
        filter.RecordOutcome(Job, succeeded: false, error: new TimeoutException());

        ConsecutiveFailures()[Job].Should().Be(2);
    }

    // Reads finance_job_consecutive_failures the way the OpenTelemetry/Prometheus pipeline would.
    private Dictionary<string, int> ConsecutiveFailures()
    {
        var observed = new Dictionary<string, int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobMetrics.MeterName && instrument.Name == "finance_job_consecutive_failures")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "job")
                    observed[(string)tag.Value!] = value;
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return observed;
    }

    private ConsecutiveFailureAlertFilter Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_banking.Object);
        services.AddSingleton(_generator.Object);
        return new ConsecutiveFailureAlertFilter(services.BuildServiceProvider(), _store, _metrics, Threshold);
    }

    private static void Fail(ConsecutiveFailureAlertFilter filter, int times)
    {
        for (var i = 0; i < times; i++)
            filter.RecordOutcome(Job, succeeded: false, error: new Exception("boom"));
    }

    private sealed class InMemoryStreakStore : IJobFailureStreakStore
    {
        private readonly Dictionary<string, JobFailureStreak> _map = [];

        public JobFailureStreak Get(string jobName)
            => _map.TryGetValue(jobName, out var streak) ? streak : JobFailureStreak.Empty;

        public void Set(string jobName, JobFailureStreak streak) => _map[jobName] = streak;
    }
}

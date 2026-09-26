namespace FinanceSentry.Tests.Unit.Observability;

using FinanceSentry.Core.Interfaces;
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
public class ConsecutiveFailureAlertFilterTests
{
    private const int Threshold = 3;
    private const string Job = "SyncScheduler.ScheduleAllActiveAccounts";

    private readonly Mock<IAlertGeneratorService> _generator = new();
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly InMemoryStreakStore _store = new();

    public ConsecutiveFailureAlertFilterTests()
    {
        _banking
            .Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[Guid.NewGuid()]);
    }

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

    private ConsecutiveFailureAlertFilter Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_banking.Object);
        services.AddSingleton(_generator.Object);
        return new ConsecutiveFailureAlertFilter(services.BuildServiceProvider(), _store, Threshold);
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

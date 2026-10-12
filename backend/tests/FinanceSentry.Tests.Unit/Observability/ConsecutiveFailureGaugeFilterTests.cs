namespace FinanceSentry.Tests.Unit.Observability;

using System.Diagnostics.Metrics;
using FinanceSentry.Infrastructure.Observability;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FluentAssertions;
using Xunit;

/// <summary>
/// Unit tests for the consecutive-failure streak that feeds <c>finance_job_consecutive_failures{job}</c>
/// (the gauge the Grafana rule alerts on): a terminal failure increments it, a success resets it to 0,
/// transient errors are ignored, and the streak survives a restart through the store.
/// </summary>
public class ConsecutiveFailureGaugeFilterTests : IDisposable
{
    private const string Job = "SyncScheduler.ScheduleAllActiveAccounts";
    private const string OtherJob = "IBKRSyncJob.Run";

    private readonly InMemoryStreakStore _store = new();
    private readonly JobMetrics _metrics = new();

    public void Dispose() => _metrics.Dispose();

    [Fact]
    public void TerminalFailures_IncrementTheGauge_PerJob()
    {
        var filter = Build();

        Fail(filter, Job, times: 2);
        Fail(filter, OtherJob, times: 1);

        ConsecutiveFailures().Should().BeEquivalentTo(
            new Dictionary<string, int> { [Job] = 2, [OtherJob] = 1 });
        _store.Get(Job).Count.Should().Be(2);
    }

    [Fact]
    public void Success_ResetsTheGaugeToZero_AndTheNextFailureStartsANewStreak()
    {
        var filter = Build();

        Fail(filter, Job, times: 3);
        Fail(filter, OtherJob, times: 1);
        filter.RecordOutcome(Job, succeeded: true, error: null);

        ConsecutiveFailures().Should().BeEquivalentTo(
            new Dictionary<string, int> { [Job] = 0, [OtherJob] = 1 });
        _store.Get(Job).Should().Be(JobFailureStreak.Empty);

        Fail(filter, Job, times: 1);
        ConsecutiveFailures()[Job].Should().Be(1);
    }

    [Fact]
    public void Success_WithNoPriorFailure_PublishesZero()
    {
        var filter = Build();

        filter.RecordOutcome(Job, succeeded: true, error: null);

        ConsecutiveFailures().Should().BeEquivalentTo(new Dictionary<string, int> { [Job] = 0 });
    }

    [Fact]
    public void TransientFailures_AreIgnored_NeitherIncrementingNorResetting()
    {
        var filter = Build();

        Fail(filter, Job, times: 2);
        filter.RecordOutcome(Job, succeeded: false, error: new TimeoutException());

        ConsecutiveFailures()[Job].Should().Be(2);
        _store.Get(Job).Count.Should().Be(2);
    }

    [Fact]
    public void TransientFailureOnly_NeverPublishesASeries()
    {
        var filter = Build();

        filter.RecordOutcome(Job, succeeded: false, error: new TimeoutException());

        ConsecutiveFailures().Should().BeEmpty();
        _store.Get(Job).Count.Should().Be(0);
    }

    [Fact]
    public void Failure_ContinuesAStreakPersistedBeforeARestart()
    {
        _store.Set(Job, new JobFailureStreak(4, Alerted: false));

        Fail(Build(), Job, times: 1);

        ConsecutiveFailures()[Job].Should().Be(5);
    }

    [Fact]
    public void Primer_PublishesPersistedStreaks_ForRecurringJobs_WithoutNewOutcome()
    {
        _store.Set("SampleJobs.A", new JobFailureStreak(5, true));
        using var freshMetrics = new JobMetrics();

        JobFailureGaugePrimer.Seed(
            [global::Hangfire.Common.Job.FromExpression(() => SampleJobs.A()), global::Hangfire.Common.Job.FromExpression(() => SampleJobs.B()), null],
            _store,
            freshMetrics);

        ConsecutiveFailures().Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["SampleJobs.A"] = 5,
            ["SampleJobs.B"] = 0,
        });
    }

    public static class SampleJobs
    {
        public static void A() { }

        public static void B() { }
    }

    // Reads finance_job_consecutive_failures the way the OpenTelemetry/Prometheus pipeline would.
    private static Dictionary<string, int> ConsecutiveFailures()
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

    private ConsecutiveFailureGaugeFilter Build() => new(_store, _metrics);

    private static void Fail(ConsecutiveFailureGaugeFilter filter, string job, int times)
    {
        for (var i = 0; i < times; i++)
            filter.RecordOutcome(job, succeeded: false, error: new Exception("boom"));
    }

    private sealed class InMemoryStreakStore : IJobFailureStreakStore
    {
        private readonly Dictionary<string, JobFailureStreak> _map = [];

        public JobFailureStreak Get(string jobName)
            => _map.TryGetValue(jobName, out var streak) ? streak : JobFailureStreak.Empty;

        public void Set(string jobName, JobFailureStreak streak) => _map[jobName] = streak;
    }
}

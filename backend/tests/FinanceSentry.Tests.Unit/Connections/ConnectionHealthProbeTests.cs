namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// Slice S4's probe: when a suspect or Dormant connection is due one (<see cref="ConnectionHealthPolicy.ProbeDue"/>),
/// what a failed probe confirms (<see cref="ConnectionHealthPolicy.RecordProbeFailure"/>), the job that makes them
/// (<see cref="ConnectionHealthProbeJob"/>) and the recording of a probe's answer (<see cref="ConnectionHealthProbeRecorder"/>).
/// </summary>
public class ConnectionHealthProbeTests
{
    private const string Provider = "ibkr";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly ConnectionHealthOptions _options = HealthOptions.With(Provider);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly Mock<IUserAlertPreferencesReader> _preferences = new();
    private readonly ConnectionHealthSubject _subject = new(Provider, "IBKRCredential", Guid.NewGuid(), Guid.NewGuid());
    private ConnectionHealth _stored = new();

    public ConnectionHealthProbeTests() =>
        _preferences
            .Setup(p => p.GetAsync(_subject.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));

    /// <summary>A connection whose scheduled run met a suspect credential failure at <paramref name="at"/>.</summary>
    private ConnectionHealth SuspectAt(DateTimeOffset at) =>
        ConnectionHealthPolicy.RecordFailure(new ConnectionHealth(), ProviderFailure.CredentialSuspect("401"), at, _options).Health;

    private static ConnectionHealth DormantSince(DateTimeOffset lastFailureAt) => new()
    {
        State = ConnectionHealthState.Dormant,
        ConsecutiveFailures = 300,
        FirstFailureAt = lastFailureAt.AddDays(-8),
        LastFailureAt = lastFailureAt,
        LastFailureClass = FailureClass.Transient,
    };

    // ── When a probe is due ──────────────────────────────────────────────────

    [Fact]
    public void Suspect_FirstProbeIsDueAMinuteAfterTheFailure()
    {
        var health = SuspectAt(Now);

        ConnectionHealthPolicy.ProbeDue(health, Now.AddSeconds(30), _options).Should().BeNull();
        ConnectionHealthPolicy.ProbeDue(health, Now.AddMinutes(1), _options).Should().Be(ConnectionHealthProbeReason.Suspect);
    }

    [Fact]
    public void Suspect_AfterAnotherFailedAttempt_AtMostOneProbePerWindow()
    {
        // The first probe failed for another reason (an outage): it confirms nothing and is counted.
        var health = ConnectionHealthPolicy.RecordProbeFailure(
            SuspectAt(Now), ProviderFailure.Transient("503"), Now.AddMinutes(1), _options).Health;
        health.SuspectSince.Should().Be(Now);

        ConnectionHealthPolicy.ProbeDue(health, Now.AddMinutes(2), _options).Should().BeNull();
        ConnectionHealthPolicy.ProbeDue(health, Now.AddMinutes(20), _options).Should().BeNull();
        ConnectionHealthPolicy.ProbeDue(health, Now.AddMinutes(1) + _options.ProbeInterval, _options)
            .Should().Be(ConnectionHealthProbeReason.Suspect);
    }

    [Fact]
    public void Dormant_ProbeIsDueAtTheSlowedCadence()
    {
        var health = DormantSince(Now);

        ConnectionHealthPolicy.ProbeDue(health, Now.AddHours(12), _options).Should().BeNull();
        ConnectionHealthPolicy.ProbeDue(health, Now.AddDays(1), _options).Should().Be(ConnectionHealthProbeReason.Dormant);
    }

    [Fact]
    public void NotBefore_HoldsTheProbe()
    {
        var health = SuspectAt(Now) with { NotBefore = Now.AddMinutes(10) };

        ConnectionHealthPolicy.ProbeDue(health, Now.AddMinutes(5), _options).Should().BeNull();
        ConnectionHealthPolicy.ProbeDue(health, Now.AddMinutes(10), _options).Should().Be(ConnectionHealthProbeReason.Suspect);
    }

    [Fact]
    public void Healthy_Failing_AndActionRequired_AreNeverProbed()
    {
        var failing = ConnectionHealthPolicy.RecordFailure(new ConnectionHealth(), ProviderFailure.Transient("503"), Now, _options).Health;
        var lost = ConnectionHealthPolicy.RecordFailure(
            new ConnectionHealth(), ProviderFailure.CredentialDefinitive("401"), Now, _options).Health;
        lost.State.Should().Be(ConnectionHealthState.ActionRequired);

        foreach (var health in new[] { new ConnectionHealth(), failing, lost })
            ConnectionHealthPolicy.ProbeDue(health, Now.AddDays(2), _options).Should().BeNull();
    }

    // ── What a probe's answer does ───────────────────────────────────────────

    [Fact]
    public void Suspect_FailedCredentialProbe_ConfirmsTheLoss()
    {
        var evaluation = ConnectionHealthPolicy.RecordProbeFailure(
            SuspectAt(Now), ProviderFailure.CredentialSuspect("401"), Now.AddMinutes(1), _options);

        evaluation.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyActionRequired);
    }

    [Fact]
    public void NotSuspect_FailedCredentialProbe_ConfirmsNothingNew()
    {
        var evaluation = ConnectionHealthPolicy.RecordProbeFailure(
            DormantSince(Now.AddDays(-1)), ProviderFailure.CredentialSuspect("401"), Now, _options);

        evaluation.Health.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        evaluation.Health.SuspectSince.Should().Be(Now);
    }

    [Fact]
    public void Suspect_SuccessfulProbe_ClearsTheSuspicion()
    {
        var evaluation = ConnectionHealthPolicy.RecordSuccess(SuspectAt(Now), Now.AddMinutes(1));

        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        evaluation.Health.SuspectSince.Should().BeNull();
        ConnectionHealthPolicy.ProbeDue(evaluation.Health, Now.AddHours(1), _options).Should().BeNull();
    }

    [Fact]
    public void Dormant_SuccessfulProbe_LeavesItDormantOnItsCadence()
    {
        var dormant = DormantSince(Now.AddDays(-1)) with { SuspectSince = Now.AddDays(-1) };

        var evaluation = ConnectionHealthPolicy.RecordProbeSuccess(dormant, Now);

        evaluation.Health.State.Should().Be(ConnectionHealthState.Dormant);
        evaluation.Health.SuspectSince.Should().BeNull();
        evaluation.Health.Should().BeEquivalentTo(dormant with { SuspectSince = null });
        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
    }

    [Fact]
    public void Dormant_SuccessfulSlowedSync_HealsAndResolves()
    {
        var evaluation = ConnectionHealthPolicy.RecordSuccess(DormantSince(Now.AddDays(-1)), Now);

        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        evaluation.Health.ConsecutiveFailures.Should().Be(0);
        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.Resolve);
    }

    [Fact]
    public void NotDormant_SuccessfulProbe_IsAnyOtherSuccess()
    {
        var evaluation = ConnectionHealthPolicy.RecordProbeSuccess(SuspectAt(Now), Now.AddMinutes(1));

        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        evaluation.Health.SuspectSince.Should().BeNull();
    }

    // ── The recorder ─────────────────────────────────────────────────────────

    private ConnectionHealthProbeRecorder CreateRecorder() =>
        new(
            ShadowRecorders.Create(_time, _options),
            _alerts.Object,
            _preferences.Object,
            _time,
            NullLogger<ConnectionHealthProbeRecorder>.Instance);

    private Task<ConnectionHealthEvaluation?> ProbeAsync(Func<CancellationToken, Task> call, IProviderFailureClassifier classifier) =>
        CreateRecorder().ProbeAsync(
            _subject, _stored, call, classifier, (health, _) =>
            {
                _stored = health;
                return Task.CompletedTask;
            });

    private static IProviderFailureClassifier ClassifiesAs(ProviderFailure failure) =>
        Mock.Of<IProviderFailureClassifier>(c => c.Classify(It.IsAny<Exception>(), It.IsAny<ProviderFailureContext>()) == failure);

    [Fact]
    public async Task Recorder_ConfirmPath_SetsActionRequired_AndAsksToReconnect()
    {
        _stored = SuspectAt(Now);
        _time.Advance(TimeSpan.FromMinutes(1));

        await ProbeAsync(_ => throw new HttpRequestException("401"), ClassifiesAs(ProviderFailure.CredentialSuspect("401")));

        _stored.State.Should().Be(ConnectionHealthState.ActionRequired);
        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            _subject.UserId, Provider, null, null, nameof(HttpRequestException), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recorder_ClearPath_GoesHealthy_AndResolvesTheAlert()
    {
        _stored = SuspectAt(Now);
        _time.Advance(TimeSpan.FromMinutes(1));

        await ProbeAsync(_ => Task.CompletedTask, ClassifiesAs(ProviderFailure.Unknown(null)));

        _stored.State.Should().Be(ConnectionHealthState.Healthy);
        _stored.SuspectSince.Should().BeNull();
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_subject.UserId, Provider, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recorder_DormantConnection_SuccessfulProbeDoesNotHealOrResolve()
    {
        _stored = DormantSince(Now.AddDays(-1));
        var dormant = _stored;
        _time.Advance(TimeSpan.FromDays(1));

        await ProbeAsync(_ => Task.CompletedTask, ClassifiesAs(ProviderFailure.Unknown(null)));

        _stored.Should().Be(dormant);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recorder_OutageDuringTheProbe_ConfirmsNothing()
    {
        _stored = SuspectAt(Now);
        _time.Advance(TimeSpan.FromMinutes(1));

        await ProbeAsync(_ => throw new HttpRequestException("503"), ClassifiesAs(ProviderFailure.Transient("503")));

        _stored.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        _stored.SuspectSince.Should().Be(Now);
        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The job ──────────────────────────────────────────────────────────────

    private sealed class FakeProbe(params ConnectionHealthProbeTarget[] targets) : IConnectionHealthProbe
    {
        public List<(ConnectionHealthProbeTarget Target, ConnectionHealthProbeReason Reason)> Probed { get; } = [];

        public bool FailListing { get; init; }

        public Task<IReadOnlyList<ConnectionHealthProbeTarget>> ListCandidatesAsync(CancellationToken ct = default) =>
            FailListing
                ? throw new InvalidOperationException("database down")
                : Task.FromResult<IReadOnlyList<ConnectionHealthProbeTarget>>(targets);

        public Task ProbeAsync(ConnectionHealthProbeTarget target, ConnectionHealthProbeReason reason, CancellationToken ct = default)
        {
            Probed.Add((target, reason));
            return Task.CompletedTask;
        }
    }

    private ConnectionHealthProbeJob CreateJob(params IConnectionHealthProbe[] probes) =>
        new(probes, HealthOptions.Monitor(_options), _time, NullLogger<ConnectionHealthProbeJob>.Instance);

    private static ConnectionHealthProbeTarget Target(string provider, ConnectionHealth health, bool probesDormant = true) =>
        new(new ConnectionHealthSubject(provider, "Credential", Guid.NewGuid(), Guid.NewGuid()), health, probesDormant);

    [Fact]
    public async Task Job_ProbesOnlyTheConnectionsDue()
    {
        _time.Advance(TimeSpan.FromMinutes(1));
        var suspect = Target(Provider, SuspectAt(Now));
        var notYet = Target(Provider, SuspectAt(Now.AddSeconds(30)));
        var dormant = Target(Provider, DormantSince(Now.AddDays(-1)));
        var healthy = Target(Provider, new ConnectionHealth());
        var probe = new FakeProbe(suspect, notYet, dormant, healthy);

        await CreateJob(probe).ExecuteAsync();

        probe.Probed.Should().BeEquivalentTo(
            [(suspect, ConnectionHealthProbeReason.Suspect), (dormant, ConnectionHealthProbeReason.Dormant)]);
    }

    [Fact]
    public async Task Job_LeavesAShadowProviderAlone()
    {
        _time.Advance(TimeSpan.FromMinutes(1));
        var probe = new FakeProbe(Target("binance", SuspectAt(Now)), Target("binance", DormantSince(Now.AddDays(-1))));

        await CreateJob(probe).ExecuteAsync();

        probe.Probed.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_SkipsDormant_WhereTheProviderProbesOnlySuspects()
    {
        var probe = new FakeProbe(Target(Provider, DormantSince(Now.AddDays(-1)), probesDormant: false));

        await CreateJob(probe).ExecuteAsync();

        probe.Probed.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_OneModuleFailingToList_DoesNotStopTheOthers()
    {
        _time.Advance(TimeSpan.FromMinutes(1));
        var broken = new FakeProbe { FailListing = true };
        var working = new FakeProbe(Target(Provider, SuspectAt(Now)));

        await CreateJob(broken, working).ExecuteAsync();

        working.Probed.Should().ContainSingle();
    }

    [Fact]
    public async Task Job_OneProbePerWindow_AcrossRuns()
    {
        var health = SuspectAt(Now);
        var probe = new FakeProbe(Target(Provider, health));
        _time.Advance(TimeSpan.FromMinutes(7));
        await CreateJob(probe).ExecuteAsync();
        probe.Probed.Should().ContainSingle();

        // The probe failed for another reason; the next run, 30 minutes on, is the next window.
        var afterFailedProbe = ConnectionHealthPolicy.RecordProbeFailure(
            health, ProviderFailure.Transient("503"), _time.GetUtcNow(), _options).Health;
        var next = new FakeProbe(Target(Provider, afterFailedProbe));
        _time.Advance(TimeSpan.FromMinutes(15));
        await CreateJob(next).ExecuteAsync();
        next.Probed.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMinutes(15));
        await CreateJob(next).ExecuteAsync();
        next.Probed.Should().ContainSingle();
    }
}

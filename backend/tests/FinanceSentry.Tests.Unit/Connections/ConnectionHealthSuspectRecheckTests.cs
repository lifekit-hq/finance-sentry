namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using FluentAssertions;
using Xunit;

/// <summary>
/// The early re-checks of a suspect credential failure (slice S4, the captain's "try again after a minute"): the attempt
/// that raised the suspicion earns one quick look a minute on, and a quick look that leaves it standing earns the
/// confirming look once the 30-minute window has passed. Nothing more, and only for an active provider. Every time is a
/// UTC offset, so nothing depends on the machine's zone.
/// </summary>
public class ConnectionHealthSuspectRecheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly ConnectionHealthOptions Options = new();

    private static ProviderFailure Suspect() => ProviderFailure.CredentialSuspect("401");

    private static ConnectionHealth SuspectSince(DateTimeOffset since, DateTimeOffset lastFailureAt) => new()
    {
        State = ConnectionHealthState.Degraded,
        ConsecutiveFailures = 2,
        FirstFailureAt = since,
        LastFailureAt = lastFailureAt,
        LastFailureClass = FailureClass.Credential,
        SuspectSince = since,
    };

    [Fact]
    public void Defaults_LookAgainAfterAMinute_ConfirmAfterHalfAnHour()
    {
        Options.SuspectQuickRetryDelay.Should().Be(TimeSpan.FromMinutes(1));
        Options.SuspectConfirmationWindow.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void FailureThatRaisesTheSuspicion_EarnsTheQuickLook()
    {
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, Options).Health;

        ConnectionHealthPolicy.SuspectRecheckAfter(before, after, recheck: 0, T0, Options)
            .Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ScheduledFailureOnAnAlreadySuspectConnection_EarnsNoSecondQuickLook()
    {
        var before = SuspectSince(T0, T0);
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0.AddMinutes(15), Options).Health;

        ConnectionHealthPolicy.SuspectRecheckAfter(before, after, recheck: 0, T0.AddMinutes(15), Options).Should().BeNull();
    }

    [Fact]
    public void QuickLookThatFails_ConfirmsNothing_AndEarnsTheConfirmingLookAtTheWindow()
    {
        var before = SuspectSince(T0, T0);
        var at = T0.AddMinutes(1);
        var evaluation = ConnectionHealthPolicy.RecordFailure(before, Suspect(), at, Options);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        ConnectionHealthPolicy.SuspectRecheckAfter(before, evaluation.Health, recheck: 1, at, Options)
            .Should().Be(TimeSpan.FromMinutes(29));
    }

    [Fact]
    public void ConfirmingLookThatFails_ConfirmsTheLoss_AndEndsTheLooks()
    {
        var before = SuspectSince(T0, T0.AddMinutes(1));
        var at = T0 + Options.SuspectConfirmationWindow;
        var evaluation = ConnectionHealthPolicy.RecordFailure(before, Suspect(), at, Options);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyActionRequired);
        ConnectionHealthPolicy.SuspectRecheckAfter(before, evaluation.Health, recheck: 2, at, Options).Should().BeNull();
    }

    [Fact]
    public void LookThatSucceeds_ClearsTheSuspicion_AndEndsTheLooks()
    {
        var before = SuspectSince(T0, T0);
        var at = T0.AddMinutes(1);
        var evaluation = ConnectionHealthPolicy.RecordSuccess(before, at);

        evaluation.Health.SuspectSince.Should().BeNull();
        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        ConnectionHealthPolicy.SuspectRecheckAfter(before, evaluation.Health, recheck: 1, at, Options).Should().BeNull();
    }

    [Fact]
    public void NoThirdLook()
    {
        var health = SuspectSince(T0, T0.AddMinutes(10));

        ConnectionHealthPolicy.SuspectRecheckAfter(health, health, recheck: 2, T0.AddMinutes(10), Options).Should().BeNull();
    }

    [Fact]
    public void QuickLookAfterTheWindowHasPassed_NeedsNoConfirmingLook()
    {
        var health = SuspectSince(T0, T0.AddMinutes(31));

        ConnectionHealthPolicy.SuspectRecheckAfter(health, health, recheck: 1, T0.AddMinutes(31), Options).Should().BeNull();
    }

    [Fact]
    public void ProviderNamedALaterWait_TheLookWaitsForIt()
    {
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, Options).Health with { NotBefore = T0.AddMinutes(5) };

        ConnectionHealthPolicy.SuspectRecheckAfter(before, after, recheck: 0, T0, Options).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(ConnectionHealthState.ActionRequired)]
    [InlineData(ConnectionHealthState.Dormant)]
    public void ConfirmedOrDormant_GetsNoLook(ConnectionHealthState state)
    {
        var health = SuspectSince(T0, T0) with { State = state };

        ConnectionHealthPolicy.SuspectRecheckAfter(new ConnectionHealth(), health, recheck: 0, T0, Options).Should().BeNull();
        ConnectionHealthSuspectRecheck.IsDue("ibkr", health, HealthOptions.With("ibkr"), T0).Should().BeFalse();
    }

    [Fact]
    public void ZeroQuickRetryDelay_TurnsTheLooksOff()
    {
        var options = new ConnectionHealthOptions { SuspectQuickRetryDelay = TimeSpan.Zero };
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, options).Health;

        ConnectionHealthPolicy.SuspectRecheckAfter(before, after, recheck: 0, T0, options).Should().BeNull();
    }

    [Fact]
    public void NonCredentialFailure_GetsNoLook()
    {
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, ProviderFailure.Transient("503"), T0, Options).Health;

        ConnectionHealthPolicy.SuspectRecheckAfter(before, after, recheck: 0, T0, Options).Should().BeNull();
    }

    // ── Scheduling: active providers only ────────────────────────────────────

    [Fact]
    public void ScheduleIfDue_ActiveProvider_SchedulesTheLook()
    {
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, Options).Health;
        var scheduled = new List<TimeSpan>();

        ConnectionHealthSuspectRecheck.ScheduleIfDue(
            new ConnectionHealthDormantCadenceTests.CapturingLogger(), Subject("ibkr"), before, after, 0, T0,
            HealthOptions.With("ibkr"), (delay, _) => scheduled.Add(delay));

        scheduled.Should().Equal(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ScheduleIfDue_ShadowProvider_MakesNoLook_AndLogsWhatItWouldDo()
    {
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, Options).Health;
        var scheduled = new List<TimeSpan>();
        var logger = new ConnectionHealthDormantCadenceTests.CapturingLogger();

        ConnectionHealthSuspectRecheck.ScheduleIfDue(
            logger, Subject("revolut_x"), before, after, 0, T0, HealthOptions.With("ibkr"), (delay, _) => scheduled.Add(delay));

        scheduled.Should().BeEmpty();
        logger.Messages.Should().ContainSingle().Which.Should().Contain("(shadow)").And.Contain("revolut_x");
    }

    [Fact]
    public void ScheduleIfDue_KeepsTheProvidersFloorBetweenCalls()
    {
        var options = new ConnectionHealthOptions { SuspectQuickRetryDelay = TimeSpan.FromSeconds(20), ActiveProviders = ["monobank"] };
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, options).Health;
        var scheduled = new List<TimeSpan>();

        ConnectionHealthSuspectRecheck.ScheduleIfDue(
            new ConnectionHealthDormantCadenceTests.CapturingLogger(), Subject("monobank"), before, after, 0, T0, options,
            (delay, _) => scheduled.Add(delay), minDelay: TimeSpan.FromSeconds(60));

        scheduled.Should().Equal(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ScheduleIfDue_SchedulerThatThrows_IsSwallowed()
    {
        var before = new ConnectionHealth();
        var after = ConnectionHealthPolicy.RecordFailure(before, Suspect(), T0, Options).Health;

        var act = () => ConnectionHealthSuspectRecheck.ScheduleIfDue(
            new ConnectionHealthDormantCadenceTests.CapturingLogger(), Subject("ibkr"), before, after, 0, T0,
            HealthOptions.With("ibkr"), (_, _) => throw new InvalidOperationException("storage down"));

        act.Should().NotThrow();
    }

    [Fact]
    public void IsDue_OnlyWhileActiveAndStillSuspect()
    {
        var suspect = SuspectSince(T0, T0);

        ConnectionHealthSuspectRecheck.IsDue("ibkr", suspect, HealthOptions.With("ibkr"), T0).Should().BeTrue();
        ConnectionHealthSuspectRecheck.IsDue("ibkr", suspect, HealthOptions.With(), T0).Should().BeFalse();
        ConnectionHealthSuspectRecheck.IsDue("ibkr", new ConnectionHealth(), HealthOptions.With("ibkr"), T0).Should().BeFalse();
        // Tried again after the look was scheduled (a sibling account's look, or a scheduled run): this look is spent.
        ConnectionHealthSuspectRecheck.IsDue("ibkr", suspect with { LastFailureAt = T0.AddSeconds(5) }, HealthOptions.With("ibkr"), T0)
            .Should().BeFalse();
    }

    private static ConnectionHealthSubject Subject(string provider) =>
        new(provider, "Credential", Guid.NewGuid(), Guid.NewGuid());
}

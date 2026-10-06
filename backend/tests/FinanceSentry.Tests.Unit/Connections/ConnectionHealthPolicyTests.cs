namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FluentAssertions;
using Xunit;

/// <summary>
/// The connection-health state machine (Option B, report §7): thresholds 3 failures / 2 h without success /
/// 7 d to Dormant, credential loss told at once, and every outcome fired once, on its transition.
/// </summary>
public class ConnectionHealthPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly ConnectionHealthOptions Options = new();

    private static readonly ProviderFailure Transient = ProviderFailure.Transient("HTTP_503");

    private static ConnectionHealthEvaluation Fail(ConnectionHealth current, DateTimeOffset at, ProviderFailure? failure = null) =>
        ConnectionHealthPolicy.RecordFailure(current, failure ?? Transient, at, Options);

    /// <summary>Records <paramref name="count"/> transient failures, <paramref name="every"/> apart, starting at T0.</summary>
    private static ConnectionHealth FailRepeatedly(int count, TimeSpan every, ConnectionHealth? start = null)
    {
        var health = start ?? new ConnectionHealth();
        for (var i = 0; i < count; i++)
            health = Fail(health, T0 + (every * i)).Health;
        return health;
    }

    // ── Defaults and options ────────────────────────────────────────────────

    [Fact]
    public void Options_DefaultToTheAcceptedThresholds()
    {
        Options.FailingMinConsecutiveFailures.Should().Be(3);
        Options.FailingMinTimeWithoutSuccess.Should().Be(TimeSpan.FromHours(2));
        Options.DormantAfter.Should().Be(TimeSpan.FromDays(7));
        ConnectionHealthOptions.SectionName.Should().Be("ConnectionHealth");
    }

    [Fact]
    public void NewHealth_IsHealthyWithNoHistory()
    {
        var health = new ConnectionHealth();

        health.State.Should().Be(ConnectionHealthState.Healthy);
        health.ConsecutiveFailures.Should().Be(0);
        health.FirstFailureAt.Should().BeNull();
    }

    // ── Degraded: the silent first step ─────────────────────────────────────

    [Fact]
    public void FirstFailure_MovesHealthyToDegraded_Silently()
    {
        var evaluation = Fail(new ConnectionHealth(), T0, ProviderFailure.Transient("HTTP_429"));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
        evaluation.Health.ConsecutiveFailures.Should().Be(1);
        evaluation.Health.FirstFailureAt.Should().Be(T0);
        evaluation.Health.LastFailureAt.Should().Be(T0);
        evaluation.Health.LastFailureClass.Should().Be(FailureClass.Transient);
        evaluation.Health.LastFailureCode.Should().Be("HTTP_429");
        evaluation.Health.StateChangedAt.Should().Be(T0);
    }

    [Fact]
    public void LaterFailure_KeepsTheFirstFailureTimeAndTheStateChangeTime()
    {
        var first = Fail(new ConnectionHealth(), T0).Health;

        var second = Fail(first, T0.AddMinutes(15)).Health;

        second.FirstFailureAt.Should().Be(T0);
        second.LastFailureAt.Should().Be(T0.AddMinutes(15));
        second.StateChangedAt.Should().Be(T0);
    }

    [Fact]
    public void FailureCode_IsTruncatedToTheColumnLength()
    {
        var evaluation = Fail(new ConnectionHealth(), T0, ProviderFailure.Unknown(new string('x', 250)));

        evaluation.Health.LastFailureCode.Should().HaveLength(ConnectionHealth.FailureCodeMaxLength);
    }

    // ── Failing: at least 3 failures AND at least 2 h without success ───────

    [Fact]
    public void ThreeFailures_InsideTwoHours_StayDegraded()
    {
        // 0, 59 min, 1 h 58 min: three failures, but only 1 h 58 min without success.
        var health = FailRepeatedly(3, TimeSpan.FromMinutes(59));

        health.State.Should().Be(ConnectionHealthState.Degraded);
        health.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public void TwoFailures_TwoHoursApart_StayDegraded()
    {
        var health = FailRepeatedly(2, TimeSpan.FromHours(3));

        health.State.Should().Be(ConnectionHealthState.Degraded);
    }

    [Fact]
    public void ThirdFailure_ExactlyTwoHoursAfterTheFirst_MovesToFailing_AndNotifiesOnce()
    {
        var two = FailRepeatedly(2, TimeSpan.FromHours(1));

        var third = Fail(two, T0.AddHours(2));

        third.Outcome.Should().Be(ConnectionHealthOutcome.NotifyFailing);
        third.Health.State.Should().Be(ConnectionHealthState.Failing);
        third.Health.StateChangedAt.Should().Be(T0.AddHours(2));

        var fourth = Fail(third.Health, T0.AddHours(3));
        fourth.Outcome.Should().Be(ConnectionHealthOutcome.None);
        fourth.Health.State.Should().Be(ConnectionHealthState.Failing);
        fourth.Health.StateChangedAt.Should().Be(T0.AddHours(2));
    }

    [Fact]
    public void ThirdFailure_OneTickShortOfTwoHours_StaysDegraded()
    {
        var two = FailRepeatedly(2, TimeSpan.FromHours(1));

        var third = Fail(two, T0.AddHours(2).AddTicks(-1));

        third.Outcome.Should().Be(ConnectionHealthOutcome.None);
        third.Health.State.Should().Be(ConnectionHealthState.Degraded);
    }

    [Fact]
    public void TimeWithoutSuccess_CountsFromTheLastSuccess_WhenThereWasOne()
    {
        // Succeeded at T0, then failures start 3 h later: the clock runs from T0, so the third failure
        // (3 h after the success) is past the 2 h even though the streak itself is only minutes old.
        var healthy = ConnectionHealthPolicy.RecordSuccess(new ConnectionHealth(), T0).Health;
        var health = healthy;
        for (var i = 0; i < 2; i++)
            health = Fail(health, T0.AddHours(3).AddMinutes(i)).Health;

        var third = Fail(health, T0.AddHours(3).AddMinutes(2));

        third.Outcome.Should().Be(ConnectionHealthOutcome.NotifyFailing);
    }

    [Fact]
    public void Thresholds_AreReadFromOptions()
    {
        var strict = new ConnectionHealthOptions
        {
            FailingMinConsecutiveFailures = 1,
            FailingMinTimeWithoutSuccess = TimeSpan.Zero,
        };

        var evaluation = ConnectionHealthPolicy.RecordFailure(new ConnectionHealth(), Transient, T0, strict);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyFailing);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Failing);
    }

    // ── Credential loss: told at once ───────────────────────────────────────

    [Fact]
    public void DefinitiveCredentialFailure_GoesStraightToActionRequired_AndNotifies()
    {
        var evaluation = Fail(new ConnectionHealth(), T0, ProviderFailure.CredentialDefinitive("MONOBANK_TOKEN_INVALID"));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyActionRequired);
        evaluation.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        evaluation.Health.LastFailureClass.Should().Be(FailureClass.Credential);
    }

    [Fact]
    public void DefinitiveCredentialFailure_FromFailing_StillNotifiesActionRequired()
    {
        var failing = FailRepeatedly(3, TimeSpan.FromHours(1));
        failing.State.Should().Be(ConnectionHealthState.Failing);

        var evaluation = Fail(failing, T0.AddHours(3), ProviderFailure.CredentialDefinitive("-2015"));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyActionRequired);
        evaluation.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
    }

    [Fact]
    public void SuspectCredentialFailure_AloneIsNotConfirmed()
    {
        var evaluation = Fail(new ConnectionHealth(), T0, ProviderFailure.CredentialSuspect("HTTP_401"));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
        evaluation.Health.SuspectSince.Should().Be(T0);
    }

    [Fact]
    public void SuspectCredentialFailure_SeenAgainInsideTheWindow_StaysUnconfirmed()
    {
        var first = Fail(new ConnectionHealth(), T0, ProviderFailure.CredentialSuspect("HTTP_401")).Health;

        var second = Fail(first, T0 + Options.SuspectConfirmationWindow - TimeSpan.FromTicks(1), ProviderFailure.CredentialSuspect("HTTP_401"));

        second.Outcome.Should().Be(ConnectionHealthOutcome.None);
        second.Health.SuspectSince.Should().Be(T0);
    }

    [Fact]
    public void SuspectCredentialFailure_SeenAgainAfterTheWindow_IsConfirmedOnce()
    {
        var first = Fail(new ConnectionHealth(), T0, ProviderFailure.CredentialSuspect("HTTP_401")).Health;

        var confirmed = Fail(first, T0 + Options.SuspectConfirmationWindow, ProviderFailure.CredentialSuspect("HTTP_401"));

        confirmed.Outcome.Should().Be(ConnectionHealthOutcome.NotifyActionRequired);
        confirmed.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        confirmed.Health.SuspectSince.Should().BeNull();
    }

    [Fact]
    public void ActionRequired_IsSticky_UntilASuccess_AndDoesNotNotifyAgain()
    {
        var actionRequired = Fail(new ConnectionHealth(), T0, ProviderFailure.CredentialDefinitive("MONOBANK_TOKEN_INVALID")).Health;

        var transient = Fail(actionRequired, T0.AddHours(3));
        var credentialAgain = Fail(transient.Health, T0.AddHours(4), ProviderFailure.CredentialDefinitive("MONOBANK_TOKEN_INVALID"));
        var aWeekLater = Fail(credentialAgain.Health, T0.AddDays(8));

        transient.Outcome.Should().Be(ConnectionHealthOutcome.None);
        credentialAgain.Outcome.Should().Be(ConnectionHealthOutcome.None);
        aWeekLater.Outcome.Should().Be(ConnectionHealthOutcome.None);
        aWeekLater.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        aWeekLater.Health.ConsecutiveFailures.Should().Be(4);
        aWeekLater.Health.StateChangedAt.Should().Be(T0);
    }

    // ── Dormant: 7 days of failing ──────────────────────────────────────────

    [Fact]
    public void FailingForExactlySevenDays_GoesDormant_Once()
    {
        var failing = FailRepeatedly(3, TimeSpan.FromHours(1));

        var dormant = Fail(failing, T0.AddDays(7));
        var stillDormant = Fail(dormant.Health, T0.AddDays(8));

        dormant.Outcome.Should().Be(ConnectionHealthOutcome.GoDormant);
        dormant.Health.State.Should().Be(ConnectionHealthState.Dormant);
        dormant.Health.StateChangedAt.Should().Be(T0.AddDays(7));
        stillDormant.Outcome.Should().Be(ConnectionHealthOutcome.None);
        stillDormant.Health.State.Should().Be(ConnectionHealthState.Dormant);
    }

    [Fact]
    public void FailingForOneTickShortOfSevenDays_StaysFailing()
    {
        var failing = FailRepeatedly(3, TimeSpan.FromHours(1));

        var evaluation = Fail(failing, T0.AddDays(7).AddTicks(-1));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Failing);
    }

    [Fact]
    public void SparseFailuresSpanningSevenDays_GoDormantWithoutBeingToldFailingFirst()
    {
        // Two failures a week apart: never 3 failures, but the streak has lasted 7 days with no success.
        var first = Fail(new ConnectionHealth(), T0).Health;

        var evaluation = Fail(first, T0.AddDays(7));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.GoDormant);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Dormant);
    }

    [Fact]
    public void DormantAfter_IsReadFromOptions()
    {
        var options = new ConnectionHealthOptions { DormantAfter = TimeSpan.FromDays(1) };
        var first = ConnectionHealthPolicy.RecordFailure(new ConnectionHealth(), Transient, T0, options).Health;

        var evaluation = ConnectionHealthPolicy.RecordFailure(first, Transient, T0.AddDays(1), options);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.GoDormant);
    }

    // ── Success ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConnectionHealthState.Failing)]
    [InlineData(ConnectionHealthState.ActionRequired)]
    [InlineData(ConnectionHealthState.Dormant)]
    public void Success_AfterAToldState_ResolvesAndResets(ConnectionHealthState told)
    {
        var current = new ConnectionHealth
        {
            State = told,
            ConsecutiveFailures = 5,
            FirstFailureAt = T0,
            LastFailureAt = T0.AddHours(5),
            LastFailureClass = FailureClass.Transient,
            LastFailureCode = "HTTP_503",
            SuspectSince = T0.AddHours(4),
            StateChangedAt = T0.AddHours(2),
        };

        var evaluation = ConnectionHealthPolicy.RecordSuccess(current, T0.AddHours(6));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.Resolve);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        evaluation.Health.ConsecutiveFailures.Should().Be(0);
        evaluation.Health.FirstFailureAt.Should().BeNull();
        evaluation.Health.SuspectSince.Should().BeNull();
        evaluation.Health.LastSuccessAt.Should().Be(T0.AddHours(6));
        evaluation.Health.StateChangedAt.Should().Be(T0.AddHours(6));
        // The last failure stays on record for diagnosis.
        evaluation.Health.LastFailureAt.Should().Be(T0.AddHours(5));
        evaluation.Health.LastFailureCode.Should().Be("HTTP_503");
    }

    [Fact]
    public void Success_AfterDegraded_ResetsSilently()
    {
        var degraded = Fail(new ConnectionHealth(), T0).Health;

        var evaluation = ConnectionHealthPolicy.RecordSuccess(degraded, T0.AddMinutes(15));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        evaluation.Health.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Success_WhenHealthy_OnlyMovesTheLastSuccess()
    {
        var healthy = ConnectionHealthPolicy.RecordSuccess(new ConnectionHealth(), T0).Health;

        var evaluation = ConnectionHealthPolicy.RecordSuccess(healthy, T0.AddMinutes(15));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.Should().Be(healthy with { LastSuccessAt = T0.AddMinutes(15) });
    }

    [Fact]
    public void Success_ResetsTheStreak_SoTheNextFailuresStartOver()
    {
        var degraded = FailRepeatedly(2, TimeSpan.FromHours(1));
        var healthy = ConnectionHealthPolicy.RecordSuccess(degraded, T0.AddHours(2)).Health;

        // One more failure 3 h later would have been the third: after the success it is the first.
        var evaluation = Fail(healthy, T0.AddHours(5));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
        evaluation.Health.ConsecutiveFailures.Should().Be(1);
    }

    // ── Internal failures are ours ──────────────────────────────────────────

    [Fact]
    public void InternalFailure_LeavesTheHealthUntouched()
    {
        var degraded = FailRepeatedly(2, TimeSpan.FromHours(1));

        var evaluation = Fail(degraded, T0.AddHours(5), ProviderFailure.Internal("MONOBANK_PARSE_ERROR"));

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.Should().BeSameAs(degraded);
    }
}

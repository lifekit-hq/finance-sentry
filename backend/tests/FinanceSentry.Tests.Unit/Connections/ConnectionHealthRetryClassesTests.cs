namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FluentAssertions;
using Xunit;

/// <summary>
/// The RateLimited and NotReady classes of the connection-health policy (report §5.7): a rate limit counts like a
/// transient failure and may name a time before which no scheduled call goes out; a not-ready answer is not counted
/// until the last success is older than the source's deadline.
/// </summary>
public class ConnectionHealthRetryClassesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly ConnectionHealthOptions Options = new();

    private static readonly ConnectionHealthSourceOptions Flex = new()
    {
        NotReadyDeadline = TimeSpan.FromHours(48),
        NotReadyRetryAfter = TimeSpan.FromHours(2),
    };

    private static ConnectionHealthEvaluation Fail(
        ConnectionHealth current,
        ProviderFailure failure,
        DateTimeOffset at,
        ConnectionHealthSourceOptions? source = null,
        DateTimeOffset? connectedAt = null) =>
        ConnectionHealthPolicy.RecordFailure(current, failure, at, Options, source, connectedAt);

    private static ConnectionHealth SucceededAt(DateTimeOffset at) =>
        ConnectionHealthPolicy.RecordSuccess(new ConnectionHealth(), at).Health;

    // ── RateLimited ─────────────────────────────────────────────────────────

    [Fact]
    public void RateLimited_CountsLikeATransientFailure_AndMovesHealthyToDegradedSilently()
    {
        var evaluation = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429"), T0);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
        evaluation.Health.ConsecutiveFailures.Should().Be(1);
        evaluation.Health.FirstFailureAt.Should().Be(T0);
        evaluation.Health.LastFailureAt.Should().Be(T0);
        evaluation.Health.LastFailureClass.Should().Be(FailureClass.RateLimited);
        evaluation.Health.LastFailureCode.Should().Be("HTTP_429");
    }

    [Fact]
    public void RateLimited_ReachesFailingOnTheSameThresholdsAsATransientFailure()
    {
        var health = new ConnectionHealth();
        ConnectionHealthEvaluation evaluation = null!;
        for (var i = 0; i < 3; i++)
        {
            evaluation = Fail(health, ProviderFailure.RateLimited("HTTP_429"), T0 + TimeSpan.FromHours(i));
            health = evaluation.Health;
        }

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyFailing);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Failing);
        evaluation.Health.LastFailureClass.Should().Be(FailureClass.RateLimited);
    }

    [Fact]
    public void RateLimited_IsNeverACredentialSignal_AndLeavesAPendingSuspectionAlone()
    {
        var suspect = Fail(new ConnectionHealth(), ProviderFailure.CredentialSuspect("HTTP_401"), T0).Health;

        var evaluation = Fail(suspect, ProviderFailure.RateLimited("HTTP_429"), T0 + TimeSpan.FromHours(1));

        evaluation.Outcome.Should().NotBe(ConnectionHealthOutcome.NotifyActionRequired);
        evaluation.Health.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        evaluation.Health.SuspectSince.Should().Be(T0);
    }

    [Fact]
    public void RateLimited_NamingATime_StoresItAsNotBefore()
    {
        var notBefore = T0.AddMinutes(10);

        var evaluation = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429", notBefore), T0);

        evaluation.Health.NotBefore.Should().Be(notBefore);
    }

    [Fact]
    public void RateLimited_NamingNoTime_LeavesNoPause()
    {
        var evaluation = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("1018"), T0);

        evaluation.Health.NotBefore.Should().BeNull();
        ConnectionHealthPolicy.ScheduledRunPausedUntil(evaluation.Health, T0, Options).Should().BeNull();
    }

    [Fact]
    public void RateLimited_KeepsTheLaterOfTwoNamedTimes_WhileTheEarlierHasNotPassed()
    {
        var first = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429", T0.AddHours(2)), T0).Health;

        var shorter = Fail(first, ProviderFailure.RateLimited("HTTP_429", T0.AddMinutes(45)), T0.AddMinutes(30));
        var longer = Fail(first, ProviderFailure.RateLimited("HTTP_429", T0.AddHours(3)), T0.AddMinutes(30));

        shorter.Health.NotBefore.Should().Be(T0.AddHours(2));
        longer.Health.NotBefore.Should().Be(T0.AddHours(3));
    }

    [Fact]
    public void RateLimited_ReplacesANotBeforeThatHasAlreadyPassed()
    {
        var first = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429", T0.AddMinutes(10)), T0).Health;
        var later = T0.AddHours(1);

        var evaluation = Fail(first, ProviderFailure.RateLimited("HTTP_429", later.AddMinutes(5)), later);

        evaluation.Health.NotBefore.Should().Be(later.AddMinutes(5));
    }

    [Fact]
    public void AnotherFailureClass_LeavesAStillPendingNotBeforeInPlace()
    {
        var limited = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429", T0.AddHours(1)), T0).Health;

        var evaluation = Fail(limited, ProviderFailure.Transient("HTTP_503"), T0.AddMinutes(5));

        evaluation.Health.NotBefore.Should().Be(T0.AddHours(1));
    }

    [Fact]
    public void Success_ClearsNotBefore()
    {
        var limited = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429", T0.AddHours(1)), T0).Health;

        var evaluation = ConnectionHealthPolicy.RecordSuccess(limited, T0.AddMinutes(5));

        evaluation.Health.NotBefore.Should().BeNull();
    }

    [Fact]
    public void ActionRequired_StillTakesTheNotBeforeAFailedProbeNamed_WithoutChangingState()
    {
        var lost = Fail(new ConnectionHealth(), ProviderFailure.CredentialDefinitive("HTTP_401"), T0).Health;
        var probeAt = T0.AddDays(1);

        var evaluation = Fail(lost, ProviderFailure.RateLimited("HTTP_429", probeAt.AddHours(1)), probeAt);

        evaluation.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.NotBefore.Should().Be(probeAt.AddHours(1));
    }

    // ── Scheduled runs and NotBefore ────────────────────────────────────────

    [Fact]
    public void ScheduledRun_WaitsForNotBefore_ThenGoesAhead()
    {
        var limited = Fail(new ConnectionHealth(), ProviderFailure.RateLimited("HTTP_429", T0.AddMinutes(10)), T0).Health;

        ConnectionHealthPolicy.ScheduledRunPausedUntil(limited, T0.AddMinutes(9), Options).Should().Be(T0.AddMinutes(10));
        ConnectionHealthPolicy.ScheduledRunPausedUntil(limited, T0.AddMinutes(10), Options).Should().BeNull();
        ConnectionHealthPolicy.ScheduledRunPausedUntil(limited, T0.AddMinutes(11), Options).Should().BeNull();
    }

    [Fact]
    public void ScheduledRun_OfAHealthyConnectionWithNoNotBefore_GoesAhead()
    {
        ConnectionHealthPolicy.ScheduledRunPausedUntil(new ConnectionHealth(), T0, Options).Should().BeNull();
    }

    [Fact]
    public void ScheduledRun_TakesTheLaterOfNotBeforeAndTheActionRequiredProbe()
    {
        var lost = Fail(new ConnectionHealth(), ProviderFailure.CredentialDefinitive("HTTP_401"), T0).Health;
        var probeAt = T0.AddDays(1);

        var earlierThanProbe = lost with { NotBefore = T0.AddHours(3) };
        var laterThanProbe = lost with { NotBefore = probeAt.AddHours(2) };

        ConnectionHealthPolicy.ScheduledRunPausedUntil(earlierThanProbe, T0.AddHours(1), Options).Should().Be(probeAt);
        ConnectionHealthPolicy.ScheduledRunPausedUntil(laterThanProbe, probeAt, Options).Should().Be(probeAt.AddHours(2));
    }

    [Fact]
    public void NotBeforePausedUntil_IgnoresTheActionRequiredProbe()
    {
        var lost = Fail(new ConnectionHealth(), ProviderFailure.CredentialDefinitive("HTTP_401"), T0).Health;

        ConnectionHealthPolicy.NotBeforePausedUntil(lost, T0.AddHours(1)).Should().BeNull();
        ConnectionHealthPolicy.NotBeforePausedUntil(lost with { NotBefore = T0.AddHours(2) }, T0.AddHours(1))
            .Should().Be(T0.AddHours(2));
    }

    // ── NotReady ────────────────────────────────────────────────────────────

    [Fact]
    public void NotReady_WithinTheDeadline_IsNotCounted_AndChangesNothing()
    {
        var health = SucceededAt(T0);

        var evaluation = Fail(health, ProviderFailure.NotReady("1003"), T0.AddHours(30), Flex);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.Should().Be(health);
        evaluation.Health.ConsecutiveFailures.Should().Be(0);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
    }

    [Fact]
    public void NotReady_JustUnderTheDeadline_IsNotCounted()
    {
        var health = SucceededAt(T0);

        var evaluation = Fail(health, ProviderFailure.NotReady("1003"), T0.AddHours(48).AddTicks(-1), Flex);

        evaluation.Health.Should().Be(health);
    }

    [Fact]
    public void NotReady_AtTheDeadline_CountsLikeATransientFailure()
    {
        var health = SucceededAt(T0);

        var evaluation = Fail(health, ProviderFailure.NotReady("1003"), T0.AddHours(48), Flex);

        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
        evaluation.Health.ConsecutiveFailures.Should().Be(1);
        evaluation.Health.LastFailureClass.Should().Be(FailureClass.NotReady);
        evaluation.Health.LastFailureCode.Should().Be("1003");
    }

    [Fact]
    public void NotReady_PastTheDeadline_ReachesFailingOnTheSameThresholdsAsATransientFailure()
    {
        var health = SucceededAt(T0);
        ConnectionHealthEvaluation evaluation = null!;
        for (var day = 2; day < 5; day++)
        {
            evaluation = Fail(health, ProviderFailure.NotReady("1003"), T0.AddDays(day), Flex);
            health = evaluation.Health;
        }

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.NotifyFailing);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Failing);
    }

    [Fact]
    public void NotReady_WithinTheDeadline_DoesNotStartOrExtendAStreak_OfAnotherFailureClass()
    {
        var degraded = Fail(SucceededAt(T0), ProviderFailure.Transient("HTTP_503"), T0.AddHours(1), Flex).Health;

        var evaluation = Fail(degraded, ProviderFailure.NotReady("1003"), T0.AddHours(2), Flex);

        evaluation.Health.Should().Be(degraded);
        evaluation.Health.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void NotReady_WithNoSuccessOnRecord_Counts()
    {
        var evaluation = Fail(new ConnectionHealth(), ProviderFailure.NotReady("1003"), T0, Flex);

        evaluation.Health.ConsecutiveFailures.Should().Be(1);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
    }

    [Fact]
    public void NotReady_OnAConnectionThatNeverSucceeded_IsNotCountedWithinTheDeadlineOfItsStart()
    {
        var evaluation = Fail(
            new ConnectionHealth(), ProviderFailure.NotReady("1003"), T0.AddHours(30), Flex, connectedAt: T0);

        evaluation.Health.Should().Be(new ConnectionHealth());
        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
    }

    [Fact]
    public void NotReady_OnAConnectionThatNeverSucceeded_CountsOnceTheDeadlineOfItsStartHasPassed()
    {
        var evaluation = Fail(
            new ConnectionHealth(), ProviderFailure.NotReady("1003"), T0.AddHours(48), Flex, connectedAt: T0);

        evaluation.Health.ConsecutiveFailures.Should().Be(1);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Degraded);
    }

    [Fact]
    public void NotReady_TheLastSuccessOutranksTheStartOfTheConnection()
    {
        var health = SucceededAt(T0.AddDays(10));

        var evaluation = Fail(health, ProviderFailure.NotReady("1003"), T0.AddDays(10).AddHours(30), Flex, connectedAt: T0);

        evaluation.Health.Should().Be(health);
    }

    [Fact]
    public void NotReady_ForASourceWithNoDeadline_CountsAtOnce()
    {
        var health = SucceededAt(T0);

        var withoutSource = Fail(health, ProviderFailure.NotReady("1003"), T0.AddHours(1));
        var withoutDeadline = Fail(
            health, ProviderFailure.NotReady("1003"), T0.AddHours(1), new ConnectionHealthSourceOptions());

        withoutSource.Health.ConsecutiveFailures.Should().Be(1);
        withoutDeadline.Health.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void NotReady_FollowsTheConfiguredDeadline()
    {
        var health = SucceededAt(T0);
        var shorter = new ConnectionHealthSourceOptions { NotReadyDeadline = TimeSpan.FromHours(6) };

        Fail(health, ProviderFailure.NotReady("1003"), T0.AddHours(5), shorter).Health.Should().Be(health);
        Fail(health, ProviderFailure.NotReady("1003"), T0.AddHours(6), shorter).Health.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void NotReady_OnAnActionRequiredConnection_RenewsTheProbeClock_AndKeepsTheState()
    {
        var lost = Fail(SucceededAt(T0), ProviderFailure.CredentialDefinitive("1012"), T0.AddHours(1), Flex).Health;
        var probeAt = T0.AddDays(1).AddHours(1);

        var evaluation = Fail(lost, ProviderFailure.NotReady("1003"), probeAt, Flex);

        evaluation.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        evaluation.Health.LastFailureAt.Should().Be(probeAt);
    }

    [Fact]
    public void NotReady_TheFailureStillCarriesNoNotBefore()
    {
        ProviderFailure.NotReady("1003").NotBefore.Should().BeNull();
        ProviderFailure.RateLimited("1018").NotBefore.Should().BeNull();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FailureClass.Transient, true)]
    [InlineData(FailureClass.RateLimited, true)]
    [InlineData(FailureClass.NotReady, true)]
    [InlineData(FailureClass.Credential, false)]
    [InlineData(FailureClass.ResourceGone, false)]
    [InlineData(FailureClass.Internal, false)]
    [InlineData(FailureClass.Unknown, false)]
    public void IsTransientLike_NamesTheClassesToldAsAnOutage(FailureClass failureClass, bool expected) =>
        failureClass.IsTransientLike().Should().Be(expected);

    [Fact]
    public void FailureClasses_AreAppendedSoStoredValuesKeepTheirMeaning()
    {
        // The column stores the name, so order is not load-bearing; the ordinals of the old values still must not move.
        ((int)FailureClass.Unknown).Should().Be(4);
        ((int)FailureClass.RateLimited).Should().Be(5);
        ((int)FailureClass.NotReady).Should().Be(6);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(90000)]
    [InlineData(864000)]
    public void RetryAfterHint_NamesTheTimeAsGiven(int seconds)
    {
        RetryAfterHint.NotBefore(T0, TimeSpan.FromSeconds(seconds)).Should().Be(T0.AddSeconds(seconds));
    }

    [Fact]
    public void RetryAfterHint_NamesNoTimeForNoWait()
    {
        RetryAfterHint.NotBefore(T0, null).Should().BeNull();
        RetryAfterHint.NotBefore(T0, TimeSpan.Zero).Should().BeNull();
        RetryAfterHint.NotBefore(T0, TimeSpan.FromSeconds(-5)).Should().BeNull();
    }
}

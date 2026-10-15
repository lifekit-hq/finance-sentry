namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>
/// The Dormant cadence (slice S4): a connection failing for <see cref="ConnectionHealthOptions.DormantAfter"/> becomes
/// Dormant, and an active provider's scheduled runs then try it once per
/// <see cref="ConnectionHealthOptions.DormantSyncInterval"/>, through the same pause as ActionRequired. A shadow
/// provider runs as before and logs what the cadence would have done. Every time is a UTC offset, so nothing depends on
/// the machine's zone.
/// </summary>
public class ConnectionHealthDormantCadenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly ConnectionHealthOptions Options = new();

    private static ConnectionHealth Dormant(DateTimeOffset lastFailureAt) => new()
    {
        State = ConnectionHealthState.Dormant,
        ConsecutiveFailures = 700,
        FirstFailureAt = lastFailureAt - TimeSpan.FromDays(8),
        LastFailureAt = lastFailureAt,
        LastFailureClass = FailureClass.Transient,
    };

    [Fact]
    public void Defaults_SlowADormantConnectionToOnceADay()
    {
        Options.DormantSyncInterval.Should().Be(TimeSpan.FromHours(24));
        Options.DormantAfter.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void FailingForDormantAfter_GoesDormant_AndTheNextRunWaitsADay()
    {
        var health = new ConnectionHealth();
        var at = T0;
        ConnectionHealthEvaluation? evaluation = null;
        while (at - T0 < Options.DormantAfter)
        {
            evaluation = ConnectionHealthPolicy.RecordFailure(health, ProviderFailure.Transient("503"), at, Options);
            health = evaluation.Health;
            at += TimeSpan.FromHours(6);
        }

        evaluation = ConnectionHealthPolicy.RecordFailure(health, ProviderFailure.Transient("503"), at, Options);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.GoDormant);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Dormant);
        ConnectionHealthPolicy.ScheduledRunPausedUntil(evaluation.Health, at.AddMinutes(15), Options, slowDormant: true)
            .Should().Be(at + Options.DormantSyncInterval);
    }

    [Fact]
    public void Dormant_PausesUntilADayAfterTheLastFailure_ThenRuns()
    {
        var health = Dormant(T0);

        ConnectionHealthPolicy.DormantPausedUntil(health, T0.AddHours(1), Options).Should().Be(T0.AddDays(1));
        ConnectionHealthPolicy.DormantPausedUntil(health, T0.AddDays(1).AddMinutes(-6), Options).Should().Be(T0.AddDays(1));
        // A daily cron starts a few seconds before the previous run's failure was stamped: the slack lets it through.
        ConnectionHealthPolicy.DormantPausedUntil(health, T0.AddDays(1).AddMinutes(-4), Options).Should().BeNull();
        ConnectionHealthPolicy.DormantPausedUntil(health, T0.AddDays(1), Options).Should().BeNull();
    }

    [Fact]
    public void Dormant_FailedRunRenewsThePause()
    {
        var at = T0.AddDays(1);
        var evaluation = ConnectionHealthPolicy.RecordFailure(Dormant(T0), ProviderFailure.Transient("503"), at, Options);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.None);
        ConnectionHealthPolicy.DormantPausedUntil(evaluation.Health, at.AddMinutes(30), Options).Should().Be(at.AddDays(1));
    }

    [Fact]
    public void Dormant_SuccessGoesHealthy_ResolvesAndLiftsThePause()
    {
        var at = T0.AddDays(1);
        var evaluation = ConnectionHealthPolicy.RecordSuccess(Dormant(T0), at);

        evaluation.Outcome.Should().Be(ConnectionHealthOutcome.Resolve);
        evaluation.Health.State.Should().Be(ConnectionHealthState.Healthy);
        ConnectionHealthPolicy.ScheduledRunPausedUntil(evaluation.Health, at.AddMinutes(15), Options, slowDormant: true)
            .Should().BeNull();
    }

    [Fact]
    public void Dormant_WithNoFailureOnRecord_RunsAtOnce_AsAfterAReconnect()
    {
        ConnectionHealthPolicy.DormantPausedUntil(Dormant(T0) with { LastFailureAt = null }, T0.AddMinutes(1), Options)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(ConnectionHealthState.Healthy)]
    [InlineData(ConnectionHealthState.Degraded)]
    [InlineData(ConnectionHealthState.Failing)]
    public void NotDormant_IsNotSlowed(ConnectionHealthState state)
    {
        var health = Dormant(T0) with { State = state };

        ConnectionHealthPolicy.DormantPausedUntil(health, T0.AddMinutes(15), Options).Should().BeNull();
        ConnectionHealthPolicy.ScheduledRunPausedUntil(health, T0.AddMinutes(15), Options, slowDormant: true).Should().BeNull();
    }

    [Fact]
    public void ScheduledRunPause_SlowsDormantOnlyWhenAsked_ForAnActiveProvider()
    {
        var health = Dormant(T0);

        ConnectionHealthPolicy.ScheduledRunPausedUntil(health, T0.AddMinutes(15), Options).Should().BeNull();
        ConnectionHealthPolicy.ScheduledRunPausedUntil(health, T0.AddMinutes(15), Options, slowDormant: true)
            .Should().Be(T0.AddDays(1));
    }

    [Fact]
    public void ScheduledRunPause_NotBeforeLaterThanTheDormantRun_Wins()
    {
        var health = Dormant(T0) with { NotBefore = T0.AddDays(2) };

        ConnectionHealthPolicy.ScheduledRunPausedUntil(health, T0.AddMinutes(15), Options, slowDormant: true)
            .Should().Be(T0.AddDays(2));
    }

    // ── The gate the scheduled jobs ask ──────────────────────────────────────

    [Fact]
    public void Gate_ActiveProvider_SkipsTheDormantConnection_UntilItsDailyRun()
    {
        var logger = new CapturingLogger();
        var options = HealthOptions.With("ibkr");

        ConnectionHealthScheduledRunGate.ShouldSkip(logger, "ibkr", Guid.NewGuid(), Dormant(T0), T0.AddHours(1), options)
            .Should().BeTrue();
        ConnectionHealthScheduledRunGate.ShouldSkip(logger, "ibkr", Guid.NewGuid(), Dormant(T0), T0.AddDays(1), options)
            .Should().BeFalse();
    }

    [Fact]
    public void Gate_ShadowProvider_RunsTheDormantConnection_AndLogsWhatTheCadenceWouldDo()
    {
        var logger = new CapturingLogger();
        var options = HealthOptions.With("ibkr");

        ConnectionHealthScheduledRunGate.ShouldSkip(logger, "binance", Guid.NewGuid(), Dormant(T0), T0.AddHours(1), options)
            .Should().BeFalse();

        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain("(shadow)").And.Contain("Dormant").And.Contain("binance");
    }

    [Fact]
    public void Gate_ShadowProvider_ActionRequiredStillPauses_AsBefore()
    {
        var lost = new ConnectionHealth { State = ConnectionHealthState.ActionRequired, LastFailureAt = T0 };

        ConnectionHealthScheduledRunGate.ShouldSkip(
                new CapturingLogger(), "binance", Guid.NewGuid(), lost, T0.AddHours(1), HealthOptions.With())
            .Should().BeTrue();
    }

    [Fact]
    public void Gate_DormantOnly_ActiveProviderPauses_ShadowProviderLogsAndGoesAhead()
    {
        var logger = new CapturingLogger();
        var options = HealthOptions.With("truelayer");
        var at = T0.AddHours(1);

        ConnectionHealthScheduledRunGate.DormantPausedUntil(
                logger, Subject("truelayer"), Dormant(T0), at, options)
            .Should().Be(T0.AddDays(1));
        logger.Messages.Should().BeEmpty();

        ConnectionHealthScheduledRunGate.DormantPausedUntil(
                logger, Subject("monobank"), Dormant(T0), at, options)
            .Should().BeNull();
        logger.Messages.Should().ContainSingle().Which.Should().Contain("(shadow)").And.Contain("monobank");
    }

    [Fact]
    public void Gate_DormantOnly_IgnoresNotBeforeAndActionRequired_WhichTheBankAppliesItsOwnWay()
    {
        var options = HealthOptions.With("monobank");
        var held = new ConnectionHealth
        {
            State = ConnectionHealthState.ActionRequired,
            LastFailureAt = T0,
            NotBefore = T0.AddHours(2),
        };

        ConnectionHealthScheduledRunGate.DormantPausedUntil(new CapturingLogger(), Subject("monobank"), held, T0.AddHours(1), options)
            .Should().BeNull();
    }

    private static ConnectionHealthSubject Subject(string provider) =>
        new(provider, "Connection", Guid.NewGuid(), Guid.NewGuid());

    internal sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}

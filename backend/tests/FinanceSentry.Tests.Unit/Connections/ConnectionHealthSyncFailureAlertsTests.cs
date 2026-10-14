namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ConnectionHealthSyncFailureAlertsTests
{
    private const string Provider = "ibkr";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly Mock<IUserAlertPreferencesReader> _preferences = new();

    public ConnectionHealthSyncFailureAlertsTests() => GivenSyncFailureAlerts(true);

    private void GivenSyncFailureAlerts(bool on) =>
        _preferences
            .Setup(p => p.GetAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: on));

    private static ConnectionHealthEvaluation Evaluation(
        ConnectionHealthOutcome outcome,
        ConnectionHealthState previous = ConnectionHealthState.Degraded,
        FailureClass? lastFailureClass = FailureClass.Transient) =>
        new(
            new ConnectionHealth { State = previous },
            new ConnectionHealth { LastFailureClass = lastFailureClass },
            outcome);

    private Task RaiseAsync(ConnectionHealthEvaluation? evaluation) =>
        ConnectionHealthSyncFailureAlerts.RaiseIfDueAsync(
            _alerts.Object, _preferences.Object, NullLogger.Instance, _userId, Provider, evaluation, "SomeException");

    private Task ResolveAsync(ConnectionHealthEvaluation? evaluation) =>
        ConnectionHealthSyncFailureAlerts.ResolveIfRecoveredAsync(
            _alerts.Object, _preferences.Object, NullLogger.Instance, _userId, Provider, evaluation);

    private void VerifyRaised(SyncFailureClass failureClass) =>
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            _userId, Provider, null, null, "SomeException", failureClass, It.IsAny<CancellationToken>()), Times.Once);

    private void VerifyNothingGenerated() =>
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Never);

    private void VerifyNothingRaised()
    {
        VerifyNothingGenerated();
        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActionRequired_EscalatesToTheReconnectAlert()
    {
        await RaiseAsync(Evaluation(ConnectionHealthOutcome.NotifyActionRequired, lastFailureClass: FailureClass.Credential));

        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            _userId, Provider, null, null, "SomeException", It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingGenerated();
    }

    [Fact]
    public async Task FailingAfterATransientFailure_RaisesTheOutageAlert()
    {
        await RaiseAsync(Evaluation(ConnectionHealthOutcome.NotifyFailing, lastFailureClass: FailureClass.Transient));

        VerifyRaised(SyncFailureClass.Outage);
    }

    [Theory]
    [InlineData(FailureClass.Unknown)]
    [InlineData(FailureClass.ResourceGone)]
    [InlineData(FailureClass.Credential)]
    public async Task FailingAfterAnyOtherFailure_RaisesTheNeutralAlert(FailureClass lastFailureClass)
    {
        await RaiseAsync(Evaluation(ConnectionHealthOutcome.NotifyFailing, lastFailureClass: lastFailureClass));

        VerifyRaised(SyncFailureClass.Unknown);
    }

    [Theory]
    [InlineData(ConnectionHealthOutcome.None)]
    [InlineData(ConnectionHealthOutcome.GoDormant)]
    [InlineData(ConnectionHealthOutcome.Resolve)]
    public async Task AnyOtherOutcome_RaisesNothing(ConnectionHealthOutcome outcome)
    {
        await RaiseAsync(Evaluation(outcome));

        VerifyNothingRaised();
    }

    [Fact]
    public async Task AnAttemptThePolicyCouldNotRecord_RaisesNothing()
    {
        await RaiseAsync(null);

        VerifyNothingRaised();
    }

    [Fact]
    public async Task SyncFailureAlertsOff_RaiseAndResolveNothing()
    {
        GivenSyncFailureAlerts(false);

        await RaiseAsync(Evaluation(ConnectionHealthOutcome.NotifyActionRequired));
        await ResolveAsync(Evaluation(ConnectionHealthOutcome.Resolve, previous: ConnectionHealthState.Failing));

        _alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AFailingAlertService_NeverReachesTheSync()
    {
        _alerts
            .Setup(a => a.GenerateSyncFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("alerts down"));
        _alerts
            .Setup(a => a.EscalateSyncFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("alerts down"));

        await FluentActions.Invoking(() => RaiseAsync(Evaluation(ConnectionHealthOutcome.NotifyActionRequired)))
            .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(ConnectionHealthState.Degraded)]
    [InlineData(ConnectionHealthState.Failing)]
    [InlineData(ConnectionHealthState.ActionRequired)]
    [InlineData(ConnectionHealthState.Dormant)]
    public async Task SuccessAfterAnyFailure_Resolves(ConnectionHealthState previous)
    {
        // Degraded too: an alert raised before the policy drove this path may still be open.
        await ResolveAsync(Evaluation(ConnectionHealthOutcome.None, previous: previous));

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, Provider, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SuccessThePolicyCouldNotRecord_Resolves()
    {
        await ResolveAsync(null);

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, Provider, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SuccessOnAHealthyConnection_ResolvesNothing()
    {
        await ResolveAsync(Evaluation(ConnectionHealthOutcome.None, previous: ConnectionHealthState.Healthy));

        _alerts.VerifyNoOtherCalls();
    }
}

namespace FinanceSentry.Tests.Unit.BankSync.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// Slice S4 on the bank scheduler: a Dormant account or connection of an active provider syncs once a day; a shadow
/// provider's schedule is untouched. The coordinator runs over the real policy; only the sync itself is faked.
/// </summary>
public class BankDormantCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly Mock<IMonobankCredentialRepository> _credentials = new();
    private readonly Mock<ITrueLayerConnectionRepository> _connections = new();
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IScheduledSyncService> _syncService = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobs = new();
    private readonly Guid _credentialId = Guid.NewGuid();
    private readonly ConnectionHealthOptions _options = HealthOptions.With("monobank");
    private readonly BankAccount _account;
    private ConnectionHealth _credentialHealth = new();

    public BankDormantCadenceTests()
    {
        _account = new BankAccount
        {
            Provider = MonobankFailureClassifier.ProviderName,
            MonobankCredentialId = _credentialId,
            UserId = Guid.NewGuid(),
            BankName = "Monobank",
        };
        ConnectionHealthStores.Back(_credentials, _credentialId, () => _credentialHealth, h => _credentialHealth = h);
        _accounts.Setup(r => r.GetByIdUnscopedAsync(_account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_account);
    }

    private BankConnectionHealth CreateHealth() =>
        BankConnectionHealths.Create(
            _accounts.Object, _credentials.Object, _connections.Object,
            Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(), _time, _options);

    private TransactionSyncCoordinator CreateCoordinator(BankConnectionHealth health) =>
        new(_accounts.Object, _syncService.Object, _backgroundJobs.Object, health);

    private void GivenSyncSucceeds(BankConnectionHealth health) =>
        _syncService
            .Setup(s => s.PerformFullSyncAsync(_account.Id, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns(async () =>
            {
                await health.RecordSyncSuccessAsync(_account);
                return new SyncResult(true, 0, 0, null, null);
            });

    private void VerifySyncCalls(Times times) =>
        _syncService.Verify(
            s => s.PerformFullSyncAsync(_account.Id, It.IsAny<CancellationToken>(), It.IsAny<string?>()), times);

    private static ConnectionHealth DormantSince(DateTimeOffset lastFailureAt) => new()
    {
        State = ConnectionHealthState.Dormant,
        ConsecutiveFailures = 300,
        FirstFailureAt = lastFailureAt.AddDays(-8),
        LastFailureAt = lastFailureAt,
        LastFailureClass = FailureClass.Transient,
    };

    [Fact]
    public async Task Active_DormantConnection_ScheduledSyncWaitsADay_ThenRuns()
    {
        _credentialHealth = DormantSince(Now.AddMinutes(-30));
        var health = CreateHealth();
        GivenSyncSucceeds(health);

        var held = await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        held.ErrorCode.Should().Be(TransactionSyncCoordinator.DormantCode);
        VerifySyncCalls(Times.Never());

        _time.Advance(TimeSpan.FromHours(23.5));
        var ran = await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        ran.Success.Should().BeTrue();
        VerifySyncCalls(Times.Once());
        _credentialHealth.State.Should().Be(ConnectionHealthState.Healthy);
    }

    [Fact]
    public async Task Active_DormantAccount_IsSlowedAlone()
    {
        _account.ApplyHealth(DormantSince(Now.AddMinutes(-30)));

        (await CreateHealth().DormantSyncPausedUntilAsync(_account)).Should().Be(Now.AddMinutes(-30).AddDays(1));
    }

    [Fact]
    public async Task Shadow_DormantConnection_SyncsAsBefore()
    {
        _options.ActiveProviders.Clear();
        _credentialHealth = DormantSince(Now.AddMinutes(-30));
        var health = CreateHealth();
        GivenSyncSucceeds(health);

        var result = await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        result.Success.Should().BeTrue();
        VerifySyncCalls(Times.Once());
    }

    [Fact]
    public void Discovery_DormantConnection_ActiveIsSlowed_ShadowIsNot()
    {
        var connection = new BankConnectionRef(MonobankFailureClassifier.ProviderName, _credentialId, _account.UserId, "Monobank");
        var dormant = DormantSince(Now.AddMinutes(-30));

        CreateHealth().DormantPausedUntil(connection, dormant).Should().Be(Now.AddMinutes(-30).AddDays(1));

        _options.ActiveProviders.Clear();
        CreateHealth().DormantPausedUntil(connection, dormant).Should().BeNull();
    }

    [Fact]
    public void Reconnect_ClearsTheAccountsLastFailure_SoItsDormantCadenceLetsTheNextRunThrough()
    {
        _account.ApplyHealth(DormantSince(Now.AddMinutes(-30)));

        _account.MarkReconnected(Guid.NewGuid(), 10m);

        _account.Health.State.Should().Be(ConnectionHealthState.Dormant);
        _account.Health.LastFailureAt.Should().BeNull();
        ConnectionHealthPolicy.DormantPausedUntil(_account.Health, Now, _options).Should().BeNull();
    }
}

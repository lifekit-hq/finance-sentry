namespace FinanceSentry.Tests.Unit.BankSync.Application.Commands;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Connection health (Option B, S2) keys a TrueLayer "reconnect" alert on the connection id. A reconnect that moves the
/// user's accounts onto a new connection must close the alert of the connection they left, because no sync runs against
/// it again. The real policy and the real finalize handler run over a fake alert store, so what is asserted is the open
/// alerts left behind, not which calls were made.
/// </summary>
public class FinalizeTrueLayerReconnectAlertTests
{
    private const string ExternalAccountId = "tl-acc-reconnect";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly HashSet<Guid> _openAlerts = [];
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly Mock<IUserAlertPreferencesReader> _preferences = new();
    private readonly Mock<ITrueLayerConnectionRepository> _connections = new();
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly TrueLayerConnection _old;
    private readonly TrueLayerConnection _new;
    private readonly BankAccount _account;

    public FinalizeTrueLayerReconnectAlertTests()
    {
        _old = new TrueLayerConnection(_userId, "ob-old", "Old consent", "ref-old");
        _new = new TrueLayerConnection(_userId, "ob-new", "New consent", "ref-new");
        _account = new BankAccount(_userId, ExternalAccountId, "AIB", "checking", "1234", "Owner", "EUR", _userId, "truelayer")
        {
            TrueLayerConnectionId = _old.Id,
        };

        _alerts
            .Setup(a => a.EscalateSyncFailureAlertAsync(
                _userId, "truelayer", It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string _, Guid? id, string? _, string? _, CancellationToken _) => _openAlerts.Add(id!.Value))
            .Returns(Task.CompletedTask);
        _alerts
            .Setup(a => a.ResolveSyncFailureAlertAsync(_userId, "truelayer", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string _, Guid? id, CancellationToken _) => _openAlerts.Remove(id!.Value))
            .Returns(Task.CompletedTask);
        _preferences.Setup(p => p.GetAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, true));

        ConnectionHealthStores.Back(Mock.Get(_connections.Object), _old.Id, () => new ConnectionHealth(), _ => { });
        _connections.Setup(c => c.GetByReferenceUnscopedAsync("ref-new", It.IsAny<CancellationToken>())).ReturnsAsync(_new);
        _connections.Setup(c => c.UpdateAsync(It.IsAny<TrueLayerConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrueLayerConnection c, CancellationToken _) => c);
        _accounts.Setup(a => a.GetByExternalAccountIdUnscopedAsync(ExternalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_account);
    }

    private BankConnectionHealth Health(params string[] activeProviders) =>
        BankConnectionHealths.Create(
            _accounts.Object, Mock.Of<IMonobankCredentialRepository>(), _connections.Object, _alerts.Object,
            _preferences.Object, new FixedClock(Now), new ConnectionHealthOptions { ActiveProviders = [.. activeProviders] });

    private FinalizeTrueLayerConnectCommandHandler Finalizer(IBankConnectionHealth health)
    {
        var client = new Mock<ITrueLayerClient>();
        client.Setup(c => c.ExchangeCodeAsync("auth-code", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerTokenSet("access-token", "refresh-token", 3600));
        client.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo(ExternalAccountId, "Current", "EUR", "AIB", "checking", null, "1234")]);
        client.Setup(c => c.ListCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TrueLayerAccountInfo>());
        var encryption = new Mock<ICredentialEncryptionService>();
        encryption.Setup(e => e.Encrypt("refresh-token")).Returns(new EncryptionResult([1], [2], [3], 1));
        var sync = new Mock<IScheduledSyncService>();
        sync.Setup(s => s.PerformFullSyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("sync not under test"));

        return new FinalizeTrueLayerConnectCommandHandler(
            client.Object, _connections.Object, encryption.Object, _accounts.Object, sync.Object, health,
            new ConfigurationBuilder().Build(), NullLogger<FinalizeTrueLayerConnectCommandHandler>.Instance);
    }

    private async Task RaiseCredentialLossOnOldConnectionAsync(BankConnectionHealth health)
    {
        var consentLost = new TrueLayerException("TRUELAYER_AUTH", "invalid_grant", 400);
        await health.RecordConnectionFailureAsync(
            new BankConnectionRef("truelayer", _old.Id, _userId, "AIB"), consentLost);
    }

    [Fact]
    public async Task ActiveProvider_ReconnectOntoNewConnection_LeavesNoOpenAlertForTheReplacedOne()
    {
        var health = Health("truelayer");
        await RaiseCredentialLossOnOldConnectionAsync(health);
        _openAlerts.Should().BeEquivalentTo([_old.Id], because: "the confirmed credential loss told the user to reconnect");

        await Finalizer(health).Handle(new FinalizeTrueLayerConnectCommand("ref-new", "auth-code"), CancellationToken.None);

        _account.TrueLayerConnectionId.Should().Be(_new.Id);
        _openAlerts.Should().BeEmpty();
        _alerts.Verify(a => a.ResolveConnectionDormantAlertAsync(
            _userId, "truelayer", _old.Id, It.IsAny<CancellationToken>()), Times.Once,
            "a Dormant notice on the replaced consent would otherwise stay open for good");
    }

    [Fact]
    public async Task ShadowMode_ReconnectOntoNewConnection_ResolvesNothing()
    {
        var health = Health();
        await RaiseCredentialLossOnOldConnectionAsync(health);

        await Finalizer(health).Handle(new FinalizeTrueLayerConnectCommand("ref-new", "auth-code"), CancellationToken.None);

        _account.TrueLayerConnectionId.Should().Be(_new.Id);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _preferences.Verify(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActiveProvider_ReconnectOnTheSameConnection_ResolvesNothingHere()
    {
        _account.TrueLayerConnectionId = _new.Id;
        var health = Health("truelayer");

        await Finalizer(health).Handle(new FinalizeTrueLayerConnectCommand("ref-new", "auth-code"), CancellationToken.None);

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

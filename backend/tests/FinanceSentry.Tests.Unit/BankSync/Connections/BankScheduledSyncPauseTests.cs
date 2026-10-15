namespace FinanceSentry.Tests.Unit.BankSync.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// A provider's named wait (a rate limit's <c>Retry-After</c>) pauses scheduled bank syncs: on the account that met
/// it, and on every account of its connection, since Monobank charges the limit to the token, not the card.
/// </summary>
public class BankScheduledSyncPauseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly Mock<IMonobankCredentialRepository> _credentials = new();
    private readonly Mock<ITrueLayerConnectionRepository> _connections = new();
    private readonly Guid _credentialId = Guid.NewGuid();
    private ConnectionHealth _credentialHealth = new();

    private Modules.BankSync.Application.Connections.BankConnectionHealth CreateHealth()
    {
        ConnectionHealthStores.Back(_credentials, _credentialId, () => _credentialHealth, h => _credentialHealth = h);
        return BankConnectionHealths.Create(
            Mock.Of<IBankAccountRepository>(), _credentials.Object, _connections.Object,
            Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(), _time);
    }

    private BankAccount MonobankAccount(ConnectionHealth? health = null)
    {
        var account = new BankAccount { Provider = "monobank", MonobankCredentialId = _credentialId };
        if (health is not null)
            account.ApplyHealth(health);
        return account;
    }

    [Fact]
    public async Task NoWaitNamed_NothingIsPaused()
    {
        var pausedUntil = await CreateHealth().ScheduledSyncPausedUntilAsync(MonobankAccount());

        pausedUntil.Should().BeNull();
    }

    [Fact]
    public async Task WaitNamedOnTheAccount_PausesUntilThen_AndNotAfter()
    {
        var account = MonobankAccount(new ConnectionHealth { NotBefore = Now.AddMinutes(10) });
        var health = CreateHealth();

        (await health.ScheduledSyncPausedUntilAsync(account)).Should().Be(Now.AddMinutes(10));

        _time.Advance(TimeSpan.FromMinutes(10));
        (await health.ScheduledSyncPausedUntilAsync(account)).Should().BeNull();
    }

    [Fact]
    public async Task WaitNamedOnTheConnection_PausesEveryAccountOfIt()
    {
        _credentialHealth = new ConnectionHealth { NotBefore = Now.AddMinutes(1) };

        var pausedUntil = await CreateHealth().ScheduledSyncPausedUntilAsync(MonobankAccount());

        pausedUntil.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public async Task WaitsOnBothTheAccountAndTheConnection_TheLaterOneWins()
    {
        _credentialHealth = new ConnectionHealth { NotBefore = Now.AddMinutes(1) };
        var account = MonobankAccount(new ConnectionHealth { NotBefore = Now.AddMinutes(5) });

        var pausedUntil = await CreateHealth().ScheduledSyncPausedUntilAsync(account);

        pausedUntil.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public async Task ActionRequired_IsNotThisGatesBusiness_BanksParkThoseAccountsAsReauthRequired()
    {
        var account = MonobankAccount(new ConnectionHealth
        {
            State = ConnectionHealthState.ActionRequired,
            LastFailureAt = Now.AddMinutes(-5),
        });

        var pausedUntil = await CreateHealth().ScheduledSyncPausedUntilAsync(account);

        pausedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ConnectionThatCannotBeRead_PausesNothingOnItsOwn()
    {
        _credentials
            .Setup(c => c.GetHealthUnscopedAsync(_credentialId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var pausedUntil = await BankConnectionHealths.Create(
            Mock.Of<IBankAccountRepository>(), _credentials.Object, _connections.Object,
            Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(), _time)
            .ScheduledSyncPausedUntilAsync(MonobankAccount());

        pausedUntil.Should().BeNull();
    }

    [Fact]
    public async Task RateLimitedSync_NamingARetryAfter_PausesTheNextScheduledRun()
    {
        var health = CreateHealth();
        var account = MonobankAccount();
        var failure = new Modules.BankSync.Infrastructure.Monobank.MonobankException(
            "MONOBANK_RATE_LIMITED", "Monobank API rate limit exceeded.", 429)
        {
            RetryAfter = TimeSpan.FromSeconds(90),
        };

        await health.RecordSyncFailureAsync(account, failure, cancelledByCaller: false);

        (await health.ScheduledSyncPausedUntilAsync(account)).Should().Be(Now.AddSeconds(90));
    }
}

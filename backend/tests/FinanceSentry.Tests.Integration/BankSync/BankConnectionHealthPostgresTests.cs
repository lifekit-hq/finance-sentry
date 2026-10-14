namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// Connection health driving Monobank alerts (Option B, S2) against a real Postgres: the real
/// <see cref="ScheduledSyncService"/>, repositories and policy, one scoped context per sync as the container gives
/// them, with only the provider and the alert sink stubbed. The connection's health is written with
/// <c>ExecuteUpdate</c> next to the sync's tracked saves, so a stale tracked copy writing old health back would
/// show here and not against mocks.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankConnectionHealthPostgresTests : IAsyncLifetime
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(30);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly Mock<IUserAlertPreferencesReader> _preferences = new();
    private readonly Mock<IBankProvider> _provider = new();
    private TestDatabase? _database;
    private MonobankCredential _credential = null!;
    private BankAccount _card = null!;
    private BankAccount _sibling = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();

        _credential = new MonobankCredential(_userId, [1], [2], [3], 1);
        _card = MonobankCard();
        _sibling = MonobankCard();
        setup.AddRange(_credential, _card, _sibling);
        await setup.SaveChangesAsync();

        _preferences.Setup(p => p.GetAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, true));
        _provider.Setup(p => p.GetAccountsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BankAccount MonobankCard() =>
        new(_userId, $"ext-{Guid.NewGuid():N}", "Monobank", "checking", "1234", "Owner", "UAH", _userId, "monobank")
        {
            MonobankCredentialId = _credential.Id,
        };

    private BankSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private void ProviderRejectsTheToken() =>
        _provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400));

    private void ProviderFailsWith(Exception ex) =>
        _provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    private void ProviderSucceeds() =>
        _provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<TransactionCandidate>)[], (DateTime?)null));

    /// <summary>Monobank's alerts are driven by the policy (<c>ConnectionHealth:ActiveProviders</c>); false is the default shadow mode.</summary>
    private bool _policyDriven = true;

    /// <summary>One sync of <paramref name="account"/> in its own scope, as the scheduled job runs it.</summary>
    private async Task<SyncResult> SyncAsync(BankAccount account)
    {
        await using var context = CreateContext();
        var accounts = new BankAccountRepository(context);
        var monobankCredentials = new MonobankCredentialRepository(context);
        var trueLayerConnections = new TrueLayerConnectionRepository(context);

        var transactions = new Mock<ITransactionRepository>();
        transactions
            .Setup(t => t.GetAllUniqueHashesByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        transactions.Setup(t => t.GetByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var encryption = new Mock<ICredentialEncryptionService>();
        encryption
            .Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns("token");
        var dedup = new Mock<ITransactionDeduplicationService>();
        dedup
            .Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
            .Returns([]);
        var providerFactory = new Mock<IBankProviderFactory>();
        providerFactory.Setup(f => f.Resolve("monobank")).Returns(_provider.Object);

        var sync = new ScheduledSyncService(
            accounts, transactions.Object, new SyncJobRepository(context), encryption.Object, dedup.Object,
            Mock.Of<IBankSyncLogger>(), providerFactory.Object, monobankCredentials,
            trueLayerConnections, Mock.Of<ITrueLayerClient>(),
            new MonobankBalanceCache(TimeProvider.System), _alerts.Object, _preferences.Object,
            Mock.Of<IEventBus>(), Mock.Of<ITrueLayerTokenRefreshService>(),
            BankConnectionHealthFactory.Create(
                accounts, monobankCredentials, trueLayerConnections, _alerts.Object, _preferences.Object, _clock,
                _policyDriven ? ["monobank"] : []));

        return await sync.PerformFullSyncAsync(account.Id);
    }

    private async Task<(MonobankCredential Credential, BankAccount Card, BankAccount Sibling)> ReadBackAsync()
    {
        await using var verify = CreateContext();
        var credential = await verify.MonobankCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(c => c.Id == _credential.Id);
        var accounts = await verify.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToDictionaryAsync(a => a.Id);
        return (credential, accounts[_card.Id], accounts[_sibling.Id]);
    }

    private void VerifyNoSyncFailureAlert()
    {
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [DockerRequiredFact]
    public async Task Rejected_token_is_suspect_once_then_confirmed_on_the_credential_with_one_alert_and_reauth()
    {
        ProviderRejectsTheToken();

        await SyncAsync(_card);
        await SyncAsync(_sibling);

        var afterFirst = await ReadBackAsync();
        afterFirst.Credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        afterFirst.Credential.Health.ConsecutiveFailures.Should().Be(2, "each card's sync is an attempt on the shared token");
        afterFirst.Credential.Health.SuspectSince.Should().Be(_clock.GetUtcNow());
        afterFirst.Card.SyncStatus.Should().Be("failed", "a suspect rejection keeps the card syncing");
        VerifyNoSyncFailureAlert();

        _clock.Advance(Cycle);
        await SyncAsync(_card);
        await SyncAsync(_sibling);

        var confirmed = await ReadBackAsync();
        confirmed.Credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        confirmed.Card.SyncStatus.Should().Be("reauth_required");
        confirmed.Sibling.SyncStatus.Should().Be("reauth_required");
        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            _userId, "monobank", _credential.Id, "Monobank", "MONOBANK_TOKEN_INVALID",
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoSyncFailureAlertOn(_card.Id);
    }

    [DockerRequiredFact]
    public async Task Non_credential_failure_is_told_only_after_three_failures_and_two_hours_then_a_success_resolves_it()
    {
        ProviderFailsWith(new MonobankException("MONOBANK_SERVER_ERROR", "Monobank API error (502): bad gateway", 502));

        await SyncAsync(_card);
        _clock.Advance(Cycle);
        await SyncAsync(_card);
        _clock.Advance(Cycle);
        await SyncAsync(_card);
        (await ReadBackAsync()).Credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        VerifyNoSyncFailureAlert();

        _clock.Advance(TimeSpan.FromHours(1));
        await SyncAsync(_card);

        var failing = await ReadBackAsync();
        failing.Credential.Health.State.Should().Be(ConnectionHealthState.Failing);
        failing.Card.SyncStatus.Should().Be("active", "a transient failure never parks or fails the card");
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            _userId, "monobank", _credential.Id, "Monobank", "MONOBANK_SERVER_ERROR", SyncFailureClass.Outage,
            It.IsAny<CancellationToken>()), Times.Once);

        ProviderSucceeds();
        _clock.Advance(Cycle);
        (await SyncAsync(_sibling)).Success.Should().BeTrue();

        (await ReadBackAsync()).Credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, "monobank", _credential.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [DockerRequiredFact]
    public async Task Provider_wide_correlation_reads_the_other_credentials_health_columns()
    {
        // Two other users' tokens are failing right now and none of them has succeeded in the window.
        var now = _clock.GetUtcNow();
        await using (var seed = CreateContext())
        {
            for (var i = 0; i < 2; i++)
            {
                var other = new MonobankCredential(Guid.NewGuid(), [1], [2], [3], 1);
                other.ApplyHealth(new ConnectionHealth
                {
                    State = ConnectionHealthState.Degraded,
                    ConsecutiveFailures = 1,
                    FirstFailureAt = now.AddMinutes(-10),
                    LastFailureAt = now.AddMinutes(-10),
                    LastSuccessAt = now.AddHours(-3),
                    LastFailureClass = FailureClass.Credential,
                });
                seed.MonobankCredentials.Add(other);
            }

            await seed.SaveChangesAsync();
        }

        await using (var read = CreateContext())
        {
            var snapshot = await new MonobankCredentialRepository(read)
                .GetProviderHealthUnscopedAsync(_credential.Id, now - TimeSpan.FromHours(2));
            snapshot.Should().Be(new ProviderHealthSnapshot(2, false));
        }

        ProviderRejectsTheToken();
        await SyncAsync(_card);
        _clock.Advance(Cycle);
        await SyncAsync(_card);

        var read2 = await ReadBackAsync();
        read2.Credential.Health.LastFailureClass.Should().Be(FailureClass.Transient, "Monobank is refusing everyone, not this token");
        read2.Credential.Health.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        read2.Card.SyncStatus.Should().Be("active");
        VerifyNoSyncFailureAlert();
    }

    [DockerRequiredFact]
    public async Task Shadow_mode_keeps_todays_alert_and_reauth_while_the_credential_records_the_would_be_suspicion()
    {
        _policyDriven = false;
        ProviderRejectsTheToken();

        await SyncAsync(_card);

        var read = await ReadBackAsync();
        read.Card.SyncStatus.Should().Be("reauth_required", "today one rejection parks the card");
        read.Sibling.SyncStatus.Should().NotBe("reauth_required");
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            _userId, "monobank", _card.Id, "Monobank", "MONOBANK_TOKEN_INVALID", SyncFailureClass.Credential,
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoSyncFailureAlertOn(_credential.Id);
        _alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        read.Credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        read.Credential.Health.SuspectSince.Should().NotBeNull();
        read.Card.Health.LastFailureCode.Should().Be("MONOBANK_TOKEN_INVALID");
    }

    private void VerifyNoSyncFailureAlertOn(Guid referenceId) =>
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), referenceId, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Never);

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

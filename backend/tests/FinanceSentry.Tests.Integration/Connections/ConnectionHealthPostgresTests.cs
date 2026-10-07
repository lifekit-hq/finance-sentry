namespace FinanceSentry.Tests.Integration.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

/// <summary>
/// Connection health, shadow mode (Option B, S1) against a real Postgres: the migrations add the owned
/// <c>Health_*</c> columns to each module's own schema, rows that existed before the migration read back as
/// Healthy, the shadow's <c>ExecuteUpdate</c> write round-trips, and a sync run through the real recorder
/// raises exactly the alert it raised before while storing the would-be state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConnectionHealthPostgresTests : IAsyncLifetime
{
    private const string BankSyncBeforeHealth = "20260926101324_M021_CounterpartyExpectedInflow";
    private const string BrokerageSyncBeforeHealth = "20261005145713_M010_AddHoldingFlexAsOfDate";
    private const string CryptoSyncBeforeHealth = "20260926090000_M007_SoftCloseCryptoHoldings";

    private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        // Only the per-module databases below are used; each context's Migrate() creates its own.
        _database = await PostgresServer.Postgres16.ReserveDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // One database per module, so each context's migration history is its own.
    private string ConnectionString(string database) => _database!.CompanionConnectionString(database);

    private BankSyncDbContext BankSync() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(ConnectionString("bank_sync_health")).Options,
            new FixedCurrentUser(null));

    private BrokerageSyncDbContext BrokerageSync() =>
        new(new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseNpgsql(ConnectionString("brokerage_sync_health")).Options,
            new FixedCurrentUser(null));

    private CryptoSyncDbContext CryptoSync() =>
        new(new DbContextOptionsBuilder<CryptoSyncDbContext>().UseNpgsql(ConnectionString("crypto_sync_health")).Options,
            new FixedCurrentUser(null));

    /// <summary>Rolls the context back to just before the health migration and forward again, rows in place.</summary>
    private static async Task ReapplyHealthMigrationAsync(DbContext context, string migrationBefore)
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migrationBefore);
        await migrator.MigrateAsync();
    }

    private static async Task<IReadOnlyDictionary<string, string?>> HealthStateDefaultsAsync(DbContext context, string schema)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT table_name, column_default FROM information_schema.columns
                WHERE table_schema = @schema AND column_name = 'Health_State' AND is_nullable = 'NO'
                """,
                connection);
            command.Parameters.AddWithValue("schema", schema);
            var defaults = new Dictionary<string, string?>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                defaults[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            return defaults;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static ConnectionHealth ActionRequiredHealth() => new()
    {
        State = ConnectionHealthState.ActionRequired,
        ConsecutiveFailures = 2,
        FirstFailureAt = At,
        LastFailureAt = At.AddMinutes(30),
        LastSuccessAt = At.AddDays(-1),
        LastFailureClass = FailureClass.Credential,
        LastFailureCode = "MONOBANK_TOKEN_INVALID",
        SuspectSince = null,
        StateChangedAt = At.AddMinutes(30),
    };

    [DockerRequiredFact]
    public async Task BankSync_Migration_AddsHealthColumns_ExistingAccountsReadHealthy_AndTheShadowWriteRoundTrips()
    {
        var account = new BankAccount(Guid.NewGuid(), "ext-1", "Monobank", "checking", "1234", "Owner", "UAH", Guid.NewGuid(), "monobank");
        await using (var setup = BankSync())
        {
            await setup.Database.MigrateAsync();
            setup.BankAccounts.Add(account);
            await setup.SaveChangesAsync();
            await ReapplyHealthMigrationAsync(setup, BankSyncBeforeHealth);

            var defaults = await HealthStateDefaultsAsync(setup, "bank_sync");
            defaults.Keys.Should().BeEquivalentTo("BankAccounts", "MonobankCredentials", "TrueLayerConnections");
            defaults.Values.Should().AllSatisfy(d => d.Should().StartWith("'Healthy'"));
        }

        await using (var read = BankSync())
        {
            var existing = await new BankAccountRepository(read).GetByIdUnscopedAsync(account.Id);
            existing!.Health.Should().Be(new ConnectionHealth());
        }

        var health = ActionRequiredHealth();
        await using (var write = BankSync())
            await new BankAccountRepository(write).SaveHealthUnscopedAsync(account.Id, health);

        await using var verify = BankSync();
        (await new BankAccountRepository(verify).GetByIdUnscopedAsync(account.Id))!.Health.Should().Be(health);
    }

    [DockerRequiredFact]
    public async Task BrokerageSync_Migration_AddsHealthColumns_ExistingCredentialsReadHealthy_AndTheShadowWriteRoundTrips()
    {
        var userId = Guid.NewGuid();
        await using (var setup = BrokerageSync())
        {
            await setup.Database.MigrateAsync();
            setup.IBKRCredentials.Add(new IBKRCredential(
                userId, "FINSENTRY", "access-token", "dh-pem", [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1));
            setup.IBKRFlexCredentials.Add(new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1));
            await setup.SaveChangesAsync();
            await ReapplyHealthMigrationAsync(setup, BrokerageSyncBeforeHealth);

            var defaults = await HealthStateDefaultsAsync(setup, "brokerage_sync");
            defaults.Keys.Should().BeEquivalentTo("IBKRCredentials", "IBKRFlexCredentials");
            defaults.Values.Should().AllSatisfy(d => d.Should().StartWith("'Healthy'"));
        }

        var health = ActionRequiredHealth() with { LastFailureCode = "HTTP_401" };
        await using (var write = BrokerageSync())
        {
            var gateway = await new IBKRCredentialRepository(write).GetByUserIdUnscopedAsync(userId);
            var flex = await new IBKRFlexCredentialRepository(write).GetByUserIdUnscopedAsync(userId);
            gateway!.Health.Should().Be(new ConnectionHealth());
            flex!.Health.Should().Be(new ConnectionHealth());

            await new IBKRCredentialRepository(write).SaveHealthUnscopedAsync(gateway.Id, health);
        }

        await using var verify = BrokerageSync();
        (await new IBKRCredentialRepository(verify).GetByUserIdUnscopedAsync(userId))!.Health.Should().Be(health);
        (await new IBKRFlexCredentialRepository(verify).GetByUserIdUnscopedAsync(userId))!.Health.Should().Be(
            new ConnectionHealth(), "the write targets only the named credential");
    }

    [DockerRequiredFact]
    public async Task CryptoSync_RejectedKey_AlertsExactlyAsBefore_WhileTheShadowStoresTheWouldBeState()
    {
        var userId = Guid.NewGuid();
        var credential = ExchangeCredential.Create(userId, CryptoExchangeProvider.Binance, [1], [2], [3], [4], [5], [6], 1);
        await using (var setup = CryptoSync())
        {
            await setup.Database.MigrateAsync();
            setup.ExchangeCredentials.Add(credential);
            await setup.SaveChangesAsync();
            await ReapplyHealthMigrationAsync(setup, CryptoSyncBeforeHealth);

            var defaults = await HealthStateDefaultsAsync(setup, "crypto_sync");
            defaults.Keys.Should().BeEquivalentTo("ExchangeCredentials");
            defaults.Values.Should().AllSatisfy(d => d.Should().StartWith("'Healthy'"));
        }

        var syncHandler = new Mock<ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult>>();
        syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BinanceException("Invalid API-key, IP, or permissions for action.", binanceErrorCode: -2015, venueStatusCode: 401));
        var alerts = new Mock<IAlertGeneratorService>();
        var preferences = new Mock<IUserAlertPreferencesReader>();
        preferences
            .Setup(p => p.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));
        var shadow = new ConnectionHealthShadowRecorder(
            Mock.Of<IOptionsMonitor<ConnectionHealthOptions>>(m => m.CurrentValue == new ConnectionHealthOptions()),
            TimeProvider.System,
            NullLogger<ConnectionHealthShadowRecorder>.Instance);

        await using (var jobContext = CryptoSync())
        {
            var job = new BinanceSyncJob(
                new ExchangeCredentialRepository(jobContext), syncHandler.Object, alerts.Object, preferences.Object,
                new InMemoryJobFailureStreakStore(), shadow, NullLogger<BinanceSyncJob>.Instance);

            // Every user failed, so the run fails, as it did before the shadow existed.
            await job.Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<AggregateException>();
        }

        alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, CryptoExchangeProvider.Binance, null, null, It.IsAny<string?>(), SyncFailureClass.Credential, It.IsAny<CancellationToken>()),
            Times.Once);
        alerts.VerifyNoOtherCalls();

        await using var verify = CryptoSync();
        var stored = await new ExchangeCredentialRepository(verify).GetUnscopedAsync(userId, CryptoExchangeProvider.Binance);
        stored!.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        stored.Health.ConsecutiveFailures.Should().Be(1);
        stored.Health.LastFailureClass.Should().Be(FailureClass.Credential);
        stored.Health.LastFailureCode.Should().Be("-2015");
    }

    private sealed class InMemoryJobFailureStreakStore : IJobFailureStreakStore
    {
        private readonly Dictionary<string, JobFailureStreak> _streaks = [];

        public JobFailureStreak Get(string jobName) =>
            _streaks.TryGetValue(jobName, out var streak) ? streak : JobFailureStreak.Empty;

        public void Set(string jobName, JobFailureStreak streak) => _streaks[jobName] = streak;
    }
}

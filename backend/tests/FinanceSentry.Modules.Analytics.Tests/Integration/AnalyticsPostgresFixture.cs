namespace FinanceSentry.Modules.Analytics.Tests.Integration;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Brings up the analytics schema against a real Postgres so the curated views + <c>fs_readonly</c>
/// role + isolation can be exercised end-to-end (feature 033, T013). The connection string is supplied
/// via the <c>ANALYTICS_TEST_PG</c> environment variable pointing at a throwaway database with the app
/// login as owner; when it is absent every integration test skips (matches the repo convention that DB
/// integration tests are opt-in, not part of the plain <c>dotnet test</c> gate).
///
/// Setup applies each source module's migrations (so the view base tables exist), then Analytics M001
/// (which creates the views + role), then seeds two users' transactions to prove cross-user isolation.
/// </summary>
public sealed class AnalyticsPostgresFixture : IAsyncLifetime
{
    public const string EnvVar = "ANALYTICS_TEST_PG";

    public static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public string? ConnectionString { get; private set; }

    public bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    public string SkipReason => $"{EnvVar} not set — integration DB unavailable";

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable(EnvVar);
        if (!Available)
        {
            return;
        }

        var conn = ConnectionString!;

        // Base-table schemas first (order mirrors production MigrationExtensions), then Analytics.
        Migrate(new BankSyncDbContext(Opts<BankSyncDbContext>(conn, "__EFMigrationsHistory"), NoCurrentUser.Instance));
        Migrate(new CryptoSyncDbContext(Opts<CryptoSyncDbContext>(conn, "__EFMigrationsHistory"), NoCurrentUser.Instance));
        Migrate(new BrokerageSyncDbContext(Opts<BrokerageSyncDbContext>(conn, "__EFMigrationsHistory"), NoCurrentUser.Instance));
        Migrate(new BudgetsDbContext(Opts<BudgetsDbContext>(conn, "__EFMigrationsHistory")));
        Migrate(new WealthDbContext(Opts<WealthDbContext>(conn, "__ef_migrations_history_wealth")));
        Migrate(new ResearchDbContext(Opts<ResearchDbContext>(conn, "__ef_migrations_history_research"), NoCurrentUser.Instance));
        Migrate(new AnalyticsDbContext(Opts<AnalyticsDbContext>(conn, "__ef_migrations_history_analytics")));

        await SeedAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public AnalyticsDbContext NewAnalyticsContext()
        => new(Opts<AnalyticsDbContext>(ConnectionString!, "__ef_migrations_history_analytics"));

    private static DbContextOptions<T> Opts<T>(string conn, string historyTable)
        where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseNpgsql(conn, b => b.MigrationsHistoryTable(historyTable, "public"))
            .Options;

    private static void Migrate<T>(T ctx) where T : DbContext
    {
        using (ctx)
        {
            ctx.Database.Migrate();
        }
    }

    private static async Task SeedAsync(string conn)
    {
        using var db = new BankSyncDbContext(Opts<BankSyncDbContext>(conn, "__EFMigrationsHistory"), NoCurrentUser.Instance);

        // Idempotent across reruns against a persistent DB.
        if (await db.Set<Transaction>().AnyAsync(t => t.UserId == UserA || t.UserId == UserB))
        {
            return;
        }

        var accountA = new BankAccount(UserA, "ext-A", "Bank A", "checking", "1111", "Alice", "USD", UserA, "truelayer");
        var accountB = new BankAccount(UserB, "ext-B", "Bank B", "checking", "2222", "Bob", "USD", UserB, "truelayer");
        db.Set<BankAccount>().AddRange(accountA, accountB);

        // User A: 3 debits totalling 60; User B: 2 debits totalling 300.
        AddDebit(db, UserA, accountA.Id, 10m, "FOOD_AND_DRINK", "Cafe A1");
        AddDebit(db, UserA, accountA.Id, 20m, "FOOD_AND_DRINK", "Cafe A2");
        AddDebit(db, UserA, accountA.Id, 30m, "TRAVEL", "Airline A3");
        AddDebit(db, UserB, accountB.Id, 100m, "FOOD_AND_DRINK", "Cafe B1");
        AddDebit(db, UserB, accountB.Id, 200m, "TRAVEL", "Airline B2");

        await db.SaveChangesAsync();
    }

    private static void AddDebit(
        BankSyncDbContext db, Guid userId, Guid accountId, decimal amount, string category, string merchant)
    {
        db.Set<Transaction>().Add(new Transaction
        {
            AccountId = accountId,
            UserId = userId,
            Amount = amount,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            PostedDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = "debit",
            MerchantName = merchant,
            MerchantCategory = category,
            Description = merchant,
            UniqueHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
        });
    }
}

/// <summary>xUnit collection so the container/schema is built once for all integration tests.</summary>
[CollectionDefinition(Name)]
public sealed class AnalyticsPostgresCollection : ICollectionFixture<AnalyticsPostgresFixture>
{
    public const string Name = "analytics-postgres";
}

namespace FinanceSentry.Tests.Integration.Analytics;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Analytics.API.Responses;
using FinanceSentry.Modules.Analytics.Application.Queries;
using FinanceSentry.Modules.Analytics.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

/// <summary>
/// The analytics query tool scopes every query to the calling user through the transaction-local
/// <c>app.current_user_id</c> setting the curated views filter on. The submitted SQL must not be able to
/// change that setting: the guard rejects setting functions before execution, and the
/// <c>fs_readonly</c> role the query runs as has no <c>EXECUTE</c> on <c>set_config</c>, so even a
/// statement that reached the database cannot re-point the owner scope.
///
/// The schema is built by the API's own startup migrations against a fresh database, the same path
/// production takes. Requires Docker (<see cref="DockerRequiredFactAttribute"/> skips otherwise; CI has it).
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class AnalyticsQueryOwnerScopeTests : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";

    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid OtherUser = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    // Re-points the owner scope to another user from inside the submitted SELECT.
    private static readonly string RepointToOtherUserSql =
        "SELECT amount FROM analytics.v_transactions "
        + $"WHERE (SELECT set_config('app.current_user_id', '{OtherUser}', true)) IS NOT NULL "
        + "ORDER BY amount";

    private TestDatabase? _database;
    private AnalyticsApiFactory? _factory;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

        // Building the TestServer runs the ordinary startup migrations, Analytics included.
        _factory = new AnalyticsApiFactory(_database.ConnectionString);
        _factory.CreateClient().Dispose();

        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Query_CannotChangeTheOwnerScope_AtTheDatabase()
    {
        var executor = Executor();

        // Bypasses the guard on purpose: the read-only role alone must refuse the setting change.
        var repoint = () => executor.ExecuteAsync(Caller, RepointToOtherUserSql);

        (await repoint.Should().ThrowAsync<PostgresException>(
                "fs_readonly must not be able to call set_config and re-point the owner scope"))
            .Which.SqlState.Should().Be(InsufficientPrivilege);
    }

    [DockerRequiredFact]
    public async Task Query_StillSeesOnlyTheCallersRows_AfterTheSetupRuns()
    {
        // The executor's own setup (set_config before the role drop) must keep working for the login.
        var executor = Executor();

        var caller = await executor.ExecuteAsync(
            Caller, "SELECT amount FROM analytics.v_transactions ORDER BY amount");
        var other = await executor.ExecuteAsync(
            OtherUser, "SELECT amount FROM analytics.v_transactions ORDER BY amount");

        caller.Rows.Select(r => (decimal)r[0]!).Should().Equal(10m, 20m);
        other.Rows.Select(r => (decimal)r[0]!).Should().Equal(700m, 800m);
    }

    [DockerRequiredFact]
    public async Task AnalyticsQuery_ThatChangesTheOwnerScope_IsRejectedBeforeExecution()
    {
        using var scope = _factory!.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<RunAnalyticsQuery, AnalyticsQueryResponse>>();

        var response = await handler.Handle(
            new RunAnalyticsQuery(Caller, RepointToOtherUserSql), CancellationToken.None);

        response.Error.Should().Be("rejected");
        response.Rows.Should().BeNullOrEmpty();
    }

    private ReadOnlyQueryExecutor Executor()
        => new(Options.Create(new AnalyticsOptions
        {
            ReadOnlyConnectionString = _database!.ConnectionString,
        }));

    private async Task SeedAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankSyncDbContext>();

        var callerAccount = new BankAccount(
            Caller, "ext-caller", "Caller Bank", "checking", "1111", "Caller", "USD", Caller, "truelayer");
        var otherAccount = new BankAccount(
            OtherUser, "ext-other", "Other Bank", "checking", "2222", "Other", "USD", OtherUser, "truelayer");
        db.Set<BankAccount>().AddRange(callerAccount, otherAccount);

        AddDebit(db, Caller, callerAccount.Id, 10m);
        AddDebit(db, Caller, callerAccount.Id, 20m);
        AddDebit(db, OtherUser, otherAccount.Id, 700m);
        AddDebit(db, OtherUser, otherAccount.Id, 800m);

        await db.SaveChangesAsync();
    }

    private static void AddDebit(BankSyncDbContext db, Guid userId, Guid accountId, decimal amount)
    {
        db.Set<Transaction>().Add(new Transaction
        {
            AccountId = accountId,
            UserId = userId,
            Amount = amount,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            PostedDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = "debit",
            MerchantName = "Merchant",
            MerchantCategory = "FOOD_AND_DRINK",
            Description = "Merchant",
            UniqueHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
        });
    }

    private sealed class AnalyticsApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:ReadOnly", connectionString);
            builder.UseSetting("Deduplication:MasterKeyBase64",
                "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
            builder.UseSetting("Encryption:CurrentKeyVersion", "1");
            builder.UseSetting("Encryption:Keys:1",
                "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
            builder.UseSetting("Jwt:Secret",
                "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
            builder.UseSetting("Jwt:ExpiryMinutes", "60");
        }
    }
}

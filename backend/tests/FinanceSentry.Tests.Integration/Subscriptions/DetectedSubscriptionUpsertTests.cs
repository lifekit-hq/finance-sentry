namespace FinanceSentry.Tests.Integration.Subscriptions;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The subscription write is one <c>INSERT ... ON CONFLICT</c> on the unique (UserId, MerchantNameNormalized) index:
/// repeating a key updates one row, and writers racing on the same key do not fail on the index. The in-memory
/// provider enforces neither the index nor raw SQL, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DetectedSubscriptionUpsertTests : IAsyncLifetime
{
    private const string Merchant = "netflix";
    private readonly string _user = Guid.NewGuid().ToString();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // No person in scope, like the detection job that calls this repository.
    private SubscriptionsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SubscriptionsDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private DetectedSubscription NewSubscription(decimal amount, string? category = null)
    {
        var last = new DateOnly(2026, 9, 1);
        return DetectedSubscription.Create(
            _user, Merchant, "Netflix", "monthly", amount, amount, "USD",
            last, last.AddMonths(1), occurrenceCount: 3, confidenceScore: 90, category: category);
    }

    private async Task<List<DetectedSubscription>> RowsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.DetectedSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(s => s.UserId == _user).ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_new_subscription_is_inserted()
    {
        var created = NewSubscription(15.99m, "streaming");
        await using (var ctx = CreateContext())
            await new DetectedSubscriptionRepository(ctx).UpsertAsync(created);

        var row = (await RowsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(created.Id);
        row.MerchantNameDisplay.Should().Be("Netflix");
        row.AverageAmount.Should().Be(15.99m);
        row.Category.Should().Be("streaming");
        row.Status.Should().Be(SubscriptionStatus.Active);
        row.LastChargeDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [DockerRequiredFact]
    public async Task A_repeated_upsert_of_one_merchant_keeps_one_row_and_updates_it()
    {
        var first = NewSubscription(15.99m);
        await using (var ctx = CreateContext())
            await new DetectedSubscriptionRepository(ctx).UpsertAsync(first);

        // A second detection pass builds a fresh entity for the same key, as the job does after a lost race.
        var second = NewSubscription(17.99m, "streaming");
        await using (var ctx = CreateContext())
            await new DetectedSubscriptionRepository(ctx).UpsertAsync(second);

        var row = (await RowsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(first.Id, "the stored row keeps its identity");
        row.LastKnownAmount.Should().Be(17.99m);
        row.Category.Should().Be("streaming");
    }

    [DockerRequiredFact]
    public async Task A_loaded_row_changed_in_the_domain_is_written_back_and_left_unchanged_in_the_context()
    {
        await using (var ctx = CreateContext())
            await new DetectedSubscriptionRepository(ctx).UpsertAsync(NewSubscription(15.99m));

        await using (var ctx = CreateContext())
        {
            var repo = new DetectedSubscriptionRepository(ctx);
            var loaded = (await repo.FindByUserAndMerchantUnscopedAsync(_user, Merchant))!;
            loaded.MarkDismissed();
            await repo.UpsertAsync(loaded);

            ctx.Entry(loaded).State.Should().Be(EntityState.Unchanged);
        }

        var row = (await RowsAsync()).Should().ContainSingle().Subject;
        row.Status.Should().Be(SubscriptionStatus.Dismissed);
        row.DismissedAt.Should().NotBeNull();
    }

    [DockerRequiredFact]
    public async Task Repeating_the_detection_result_keeps_one_row()
    {
        var detected = new DetectedSubscriptionData(
            Merchant, "Netflix", "monthly", 12m, 12m, "USD",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), OccurrenceCount: 4, ConfidenceScore: 90, Category: null);

        for (var run = 0; run < 2; run++)
        {
            await using var ctx = CreateContext();
            await new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(ctx))
                .UpsertDetectedSubscriptionsAsync(_user, [detected]);
        }

        (await RowsAsync()).Should().ContainSingle().Which.OccurrenceCount.Should().Be(4);
    }

    [DockerRequiredFact]
    public async Task Concurrent_upserts_of_one_merchant_leave_one_row_without_failing()
    {
        var writes = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new DetectedSubscriptionRepository(ctx).UpsertAsync(NewSubscription(10m + i));
        });

        await Task.WhenAll(writes);

        (await RowsAsync()).Should().ContainSingle();
    }
}

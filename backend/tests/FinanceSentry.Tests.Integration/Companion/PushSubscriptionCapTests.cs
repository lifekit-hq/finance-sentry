namespace FinanceSentry.Tests.Integration.Companion;

using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class PushSubscriptionCapTests
{
    private readonly string _dbName = $"PushCap_{Guid.NewGuid()}";
    private readonly Guid _user = Guid.NewGuid();

    private CompanionDbContext CreateContext(Guid? actingUser) =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseInMemoryDatabase(_dbName).Options,
            new FixedCurrentUser(actingUser));

    private static PushSubscription NewSubscription(Guid userId, string endpoint, DateTimeOffset createdAt) => new()
    {
        UserId = userId,
        Endpoint = endpoint,
        P256dh = "p256dh-key",
        Auth = "auth-secret",
        CreatedAt = createdAt,
    };

    private async Task<List<PushSubscription>> SeedAsync(Guid userId, int count)
    {
        var start = DateTimeOffset.UtcNow.AddHours(-count);
        var seeded = Enumerable.Range(0, count)
            .Select(i => NewSubscription(userId, $"https://fcm.googleapis.com/fcm/send/{userId:N}-{i}", start.AddMinutes(i)))
            .ToList();
        await using var ctx = CreateContext(userId);
        ctx.PushSubscriptions.AddRange(seeded);
        await ctx.SaveChangesAsync();
        return seeded;
    }

    private async Task<List<string>> EndpointsAsync(Guid userId)
    {
        await using var ctx = CreateContext(userId);
        return await ctx.PushSubscriptions.OrderBy(s => s.CreatedAt).Select(s => s.Endpoint).ToListAsync();
    }

    [Fact]
    public async Task An_eleventh_device_replaces_the_users_oldest()
    {
        var seeded = await SeedAsync(_user, PushSubscriptionLimits.MaxPerUser);
        var others = await SeedAsync(Guid.NewGuid(), 1);
        const string fresh = "https://fcm.googleapis.com/fcm/send/new-device";

        await using (var ctx = CreateContext(_user))
            await new PushSubscriptionRepository(ctx).UpsertByEndpointAsync(
                NewSubscription(_user, fresh, DateTimeOffset.UtcNow));

        var endpoints = await EndpointsAsync(_user);
        endpoints.Should().HaveCount(PushSubscriptionLimits.MaxPerUser);
        endpoints.Should().NotContain(seeded[0].Endpoint);
        endpoints.Should().Contain([seeded[1].Endpoint, fresh]);
        (await EndpointsAsync(others[0].UserId)).Should().HaveCount(1, "another person's devices are never evicted");
    }

    [Fact]
    public async Task Re_registering_an_existing_endpoint_at_the_cap_evicts_nothing()
    {
        var seeded = await SeedAsync(_user, PushSubscriptionLimits.MaxPerUser);

        await using (var ctx = CreateContext(_user))
            await new PushSubscriptionRepository(ctx).UpsertByEndpointAsync(
                NewSubscription(_user, seeded[0].Endpoint, DateTimeOffset.UtcNow));

        (await EndpointsAsync(_user)).Should().BeEquivalentTo(seeded.Select(s => s.Endpoint));
    }

    [Fact]
    public async Task Under_the_cap_nothing_is_evicted()
    {
        var seeded = await SeedAsync(_user, PushSubscriptionLimits.MaxPerUser - 1);

        await using (var ctx = CreateContext(_user))
            await new PushSubscriptionRepository(ctx).UpsertByEndpointAsync(
                NewSubscription(_user, "https://fcm.googleapis.com/fcm/send/new-device", DateTimeOffset.UtcNow));

        (await EndpointsAsync(_user)).Should().HaveCount(seeded.Count + 1);
    }
}

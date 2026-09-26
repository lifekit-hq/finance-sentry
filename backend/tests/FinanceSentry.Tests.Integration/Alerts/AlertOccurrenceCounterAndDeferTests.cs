namespace FinanceSentry.Tests.Integration.Alerts;

using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// finance-sentry#419 S5: M004_AddOccurrenceCounter applies against a real Postgres database (the
/// hand-written migration/designer/snapshot trio is exercised end to end, not just discovered), and
/// the occurrence-bump / Defer behaviors it enables round-trip correctly. Real Postgres is required
/// for <c>Database.MigrateAsync()</c> and for <c>ExecuteUpdateAsync</c>, which the in-memory provider
/// does not support.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AlertOccurrenceCounterAndDeferTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private AlertsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AlertsDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
            .Options);

    [DockerRequiredFact]
    public async Task MigrateAsync_AppliesM004_ExistingAndNewRowsDefaultToOccurrenceCountOne()
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var seeded = new Alert
        {
            UserId = Guid.NewGuid(),
            Type = AlertType.LowBalance,
            Title = "Low balance",
            Message = "m",
        };
        await using (var seed = CreateContext())
        {
            seed.Alerts.Add(seeded);
            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var reloaded = await read.Alerts.AsNoTracking().SingleAsync(a => a.Id == seeded.Id);

        reloaded.OccurrenceCount.Should().Be(1);
        reloaded.LastOccurredAt.Should().NotBe(default);
    }

    [DockerRequiredFact]
    public async Task BumpOccurrenceAsync_IncrementsCountAndRefreshesTimestampsWithoutInsertingARow()
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var userId = Guid.NewGuid();
        var alert = new Alert
        {
            UserId = userId,
            Type = AlertType.PriceHike,
            Title = "Price hike",
            Message = "m",
            LastOccurredAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        await using (var seed = CreateContext())
        {
            seed.Alerts.Add(alert);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repository = new AlertRepository(ctx);
            await repository.BumpOccurrenceAsync(alert.Id);
        }

        await using var read = CreateContext();
        var all = await read.Alerts.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();

        all.Should().ContainSingle("a suppressed repeat must bump the existing row, not insert a new one");
        all[0].OccurrenceCount.Should().Be(2);
        all[0].LastOccurredAt.Should().BeAfter(alert.LastOccurredAt);
    }

    [DockerRequiredFact]
    public async Task AcknowledgeAsync_Defer_MarksReadWithoutResolvingSoTheExpiryJobCanExpireItLater()
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var userId = Guid.NewGuid();
        var alert = new Alert
        {
            UserId = userId,
            Type = AlertType.LowBalance,
            Title = "Low balance",
            Message = "m",
        };
        await using (var seed = CreateContext())
        {
            seed.Alerts.Add(alert);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repository = new AlertRepository(ctx);
            var ok = await repository.AcknowledgeAsync(userId, alert.Id, "Defer");
            ok.Should().BeTrue();
        }

        await using var read = CreateContext();
        var reloaded = await read.Alerts.AsNoTracking().SingleAsync(a => a.Id == alert.Id);

        reloaded.IsRead.Should().BeTrue("Defer means 'not now' — it acknowledges the alert as read");
        reloaded.IsResolved.Should().BeFalse("Defer must not resolve the alert outright; the expiry job does that after 7 days");
        reloaded.AcknowledgementDecision.Should().Be("Defer");
        reloaded.AcknowledgedAt.Should().NotBeNull();
    }
}

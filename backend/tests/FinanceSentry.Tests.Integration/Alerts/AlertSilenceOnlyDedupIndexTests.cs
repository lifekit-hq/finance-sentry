namespace FinanceSentry.Tests.Integration.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// A SilenceOnly market-structure alert (the intraday-move sentinel's mode) re-fires once its 24h
/// window has passed even while the earlier alert on the same reference is still open. Real Postgres
/// is required: the <c>idx_alert_dedup</c> partial unique index on (UserId, Type, ReferenceId) over
/// open alerts is what rejected the second insert, and the in-memory provider does not enforce it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AlertSilenceOnlyDedupIndexTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
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
    public async Task SilenceOnly_PastWindowWithEarlierAlertStillOpen_SupersedesItInsteadOfViolatingIndex()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var userId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var old = new Alert
        {
            UserId = userId,
            Type = AlertType.MarketStructure,
            Title = "Unusual move: INTC",
            Message = "earlier move",
            ReferenceId = referenceId,
            ReferenceLabel = "INTC",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-25),
        };
        await using (var seed = CreateContext())
        {
            seed.Alerts.Add(old);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var generator = new AlertGeneratorService(new AlertRepository(ctx));
            await generator.GenerateMarketStructureAlertAsync(
                userId, referenceId, "INTC", "moved 12.2% intraday", dedup: AlertDedup.SilenceOnly);
        }

        await using var read = CreateContext();
        var alerts = await read.Alerts.AsNoTracking()
            .Where(a => a.UserId == userId && a.ReferenceId == referenceId)
            .ToListAsync();

        alerts.Should().HaveCount(2);
        alerts.Single(a => a.Id == old.Id).IsResolved.Should().BeTrue("the new occurrence supersedes it");
        alerts.Single(a => a.Id != old.Id).Should().Match<Alert>(a => !a.IsResolved && !a.IsDismissed);
    }
}

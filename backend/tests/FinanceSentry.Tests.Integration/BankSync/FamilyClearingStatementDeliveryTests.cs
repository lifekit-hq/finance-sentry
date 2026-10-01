namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The monthly family-clearing statement job (#434 S5) driven through the real composer, the real
/// <see cref="AlertGeneratorService"/> and a real Postgres alert table (the <c>idx_alert_dedup</c>
/// partial unique index is not enforced by the in-memory provider). Only the classification-backed
/// statement query and the active-user list are stubbed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FamilyClearingStatementDeliveryTests : IAsyncLifetime
{
    private const int MaxTelegramLines = 12;
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-40);

    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // The job runs with no person in scope (null), as it does in production; assertion reads act as the user.
    private AlertsDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private static FamilyClearingStatement Statement(string month, int counterparties)
    {
        var lines = Enumerable.Range(1, counterparties)
            .Select(i => new CounterpartyStatementLine(
                $"Relative {i}", "family_support", 500m, 1200m, -700m,
                [new CounterpartyStatementCurrencySubtotal("USD", 500m, 1200m, -700m)]))
            .ToList();
        return new FamilyClearingStatement(
            month, lines, SupportTotalUsd: 1200m * counterparties, ReceivedTotalUsd: 500m * counterparties,
            ExcludedRoutingLegs: 2);
    }

    private async Task RunJobAsync(Guid userId, FamilyClearingStatement statement)
    {
        var users = new Mock<IBankingTotalsReader>();
        users.Setup(u => u.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([userId]);
        var query = new Mock<IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement>>();
        query.Setup(q => q.Handle(It.IsAny<GetFamilyClearingStatementQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

        await using var ctx = CreateContext();
        var job = new FamilyClearingStatementJob(
            users.Object, query.Object, new AlertGeneratorService(new AlertRepository(ctx), new Mock<IPolicyAckReader>().Object),
            NullLogger<FamilyClearingStatementJob>.Instance);
        await job.ExecuteAsync();
    }

    private async Task<List<Alert>> StatementAlertsAsync(Guid userId)
    {
        await using var read = CreateContext(userId);
        return await read.Alerts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Type == AlertType.FamilyStatement)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }

    [DockerRequiredFact]
    public async Task Job_PersistsOneInfoAlert_WithinTheTelegramLineBudget()
    {
        var userId = Guid.NewGuid();

        await RunJobAsync(userId, Statement("2026-08", counterparties: 30));

        var alert = (await StatementAlertsAsync(userId)).Should().ContainSingle().Subject;
        alert.Severity.Should().Be(AlertSeverity.Info);
        alert.Title.Should().StartWith("Family statement — 2026-08");
        (1 + alert.Message.Split('\n').Length).Should().BeLessThanOrEqualTo(MaxTelegramLines);
        alert.Message.Should().Contain("more").And.Contain("2 routing leg(s) excluded");
    }

    [DockerRequiredFact]
    public async Task Job_RerunInTheSameMonth_DoesNotDoubleAlert()
    {
        var userId = Guid.NewGuid();

        await RunJobAsync(userId, Statement("2026-08", 2));
        await RunJobAsync(userId, Statement("2026-08", 2));

        (await StatementAlertsAsync(userId)).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task Job_NextMonth_WithPreviousStatementStillUnread_AddsItsOwnRow()
    {
        var userId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            seed.Alerts.Add(new Alert
            {
                UserId = userId, Type = AlertType.FamilyStatement, Severity = AlertSeverity.Info,
                Title = "Family statement — 2026-07", Message = "unread", ReferenceLabel = "monthly",
                CreatedAt = LongAgo,
            });
            await seed.SaveChangesAsync();
        }

        await RunJobAsync(userId, Statement("2026-08", 2));

        var alerts = await StatementAlertsAsync(userId);
        alerts.Should().HaveCount(2);
        alerts.Should().OnlyContain(a => !a.IsResolved && !a.IsDismissed, "an unread statement must not swallow the next");
    }

    [DockerRequiredFact]
    public async Task Job_EmptyMonth_StillDeliversAnHonestEmptyStatement()
    {
        var userId = Guid.NewGuid();

        await RunJobAsync(userId, new FamilyClearingStatement("2026-08", [], 0m, 0m, 0));

        var alert = (await StatementAlertsAsync(userId)).Should().ContainSingle().Subject;
        alert.Message.Should().Contain("No family-support activity this month.");
    }
}

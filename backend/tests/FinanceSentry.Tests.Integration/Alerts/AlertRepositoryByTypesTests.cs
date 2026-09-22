namespace FinanceSentry.Tests.Integration.Alerts;

using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>049: the by-types read is non-dismissed, type-filtered, newest first, paged.</summary>
public sealed class AlertRepositoryByTypesTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();
    private readonly AlertsDbContext _db;
    private readonly AlertRepository _repo;

    public AlertRepositoryByTypesTests()
    {
        _db = new AlertsDbContext(new DbContextOptionsBuilder<AlertsDbContext>()
            .UseInMemoryDatabase($"alerts-{Guid.NewGuid():N}").Options);
        _repo = new AlertRepository(_db);
    }

    private Alert Add(string type, int minutesAgo, bool dismissed = false, Guid? user = null)
    {
        var a = new Alert
        {
            UserId = user ?? User, Type = type, Severity = AlertSeverity.Info, Title = type, Message = "m",
            IsDismissed = dismissed, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        };
        _db.Alerts.Add(a);
        return a;
    }

    [Fact]
    public async Task Filters_by_type_and_user_excludes_dismissed_and_orders_newest_first()
    {
        var older = Add(AlertType.NewsCluster, 60);
        var newer = Add(AlertType.EarningsAhead, 5);
        Add(AlertType.SyncFailure, 1);
        Add(AlertType.NewsCluster, 2, dismissed: true);
        Add(AlertType.NewsCluster, 3, user: Guid.NewGuid());
        await _db.SaveChangesAsync();

        var (items, total) = await _repo.GetByTypesPagedAsync(User, [AlertType.NewsCluster, AlertType.EarningsAhead], 1, 10);

        total.Should().Be(2);
        items.Select(a => a.Id).Should().Equal(newer.Id, older.Id);
    }

    [Fact]
    public async Task Pages_with_a_total_that_spans_pages()
    {
        for (var i = 0; i < 5; i++) Add(AlertType.BudgetBreach, i);
        await _db.SaveChangesAsync();

        var (page2, total) = await _repo.GetByTypesPagedAsync(User, [AlertType.BudgetBreach], 2, 2);

        total.Should().Be(5);
        page2.Should().HaveCount(2);
    }

    public void Dispose() => _db.Dispose();
}

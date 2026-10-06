namespace FinanceSentry.Tests.Integration.Alerts;

using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The Companion relay asks which SyncFailure alerts are still open: a resolved, dismissed or deleted alert must drop
/// out of the answer so its held or pending event expires instead of going out as a live outage.
/// </summary>
public sealed class MaterialAlertReaderOpenIdsTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();
    private readonly AlertsDbContext _db;
    private readonly MaterialAlertReader _reader;

    public MaterialAlertReaderOpenIdsTests()
    {
        _db = new AlertsDbContext(new DbContextOptionsBuilder<AlertsDbContext>()
            .UseInMemoryDatabase($"alerts-open-{Guid.NewGuid():N}").Options, new FixedCurrentUser(User));
        _reader = new MaterialAlertReader(_db);
    }

    private Alert Add(bool dismissed = false, DateTimeOffset? resolvedAt = null, Guid? user = null)
    {
        var a = new Alert
        {
            UserId = user ?? User, Type = AlertType.SyncFailure, Severity = AlertSeverity.Warning, Title = "Sync failed",
            Message = "m", IsDismissed = dismissed, ResolvedAt = resolvedAt,
        };
        _db.Alerts.Add(a);
        return a;
    }

    [Fact]
    public async Task An_open_alert_is_reported_and_a_resolved_one_is_not()
    {
        var open = Add();
        var resolved = Add(resolvedAt: DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        var ids = await _reader.GetOpenIdsAsync([open.Id, resolved.Id]);

        ids.Should().BeEquivalentTo([open.Id]);
    }

    [Fact]
    public async Task A_dismissed_alert_is_not_open()
    {
        var dismissed = Add(dismissed: true);
        await _db.SaveChangesAsync();

        var ids = await _reader.GetOpenIdsAsync([dismissed.Id]);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task An_alert_deleted_with_its_account_is_not_open()
    {
        var gone = Add();
        await _db.SaveChangesAsync();
        _db.Alerts.Remove(gone);
        await _db.SaveChangesAsync();

        var ids = await _reader.GetOpenIdsAsync([gone.Id, Guid.NewGuid()]);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_users_open_alert_is_still_seen_by_the_no_principal_relay()
    {
        var other = Add(user: Guid.NewGuid());
        await _db.SaveChangesAsync();

        var ids = await _reader.GetOpenIdsAsync([other.Id]);

        ids.Should().BeEquivalentTo([other.Id]);
    }

    [Fact]
    public async Task An_empty_request_returns_nothing_without_querying()
    {
        var ids = await _reader.GetOpenIdsAsync([]);

        ids.Should().BeEmpty();
    }

    public void Dispose() => _db.Dispose();
}

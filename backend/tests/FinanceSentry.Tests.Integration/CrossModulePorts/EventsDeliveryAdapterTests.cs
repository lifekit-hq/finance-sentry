namespace FinanceSentry.Tests.Integration.CrossModulePorts;

using FinanceSentry.Integration;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// 049: the delivery port finds an alert's outbox row by the dedup key capture wrote, reads the alert
/// id back from it, and never returns another user's row.
/// </summary>
public sealed class EventsDeliveryAdapterTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();
    private readonly CompanionDbContext _db;
    private readonly EventsDeliveryAdapter _adapter;
    private readonly MaterialityPolicy _policy = new();

    public EventsDeliveryAdapterTests()
    {
        _db = new CompanionDbContext(new DbContextOptionsBuilder<CompanionDbContext>()
            .UseInMemoryDatabase($"companion-{Guid.NewGuid():N}").Options);
        _adapter = new EventsDeliveryAdapter(new CompanionEventRepository(_db), _policy);
    }

    private async Task<CompanionEvent> Captured(Guid userId, Guid alertId, EventDisposition disposition)
    {
        var evt = new CompanionEvent
        {
            UserId = userId,
            Kind = CompanionEventKind.NewsCluster,
            Subject = "MU",
            Severity = "Warning",
            Summary = "News cluster: MU",
            DedupKey = _policy.AlertDedupKey(alertId),
            ReferenceId = alertId,
            SourceModule = "alerts",
            Disposition = disposition,
            OccurredAt = DateTimeOffset.UtcNow,
            CapturedAt = DateTimeOffset.UtcNow,
            DeliveredAt = disposition == EventDisposition.Delivered ? DateTimeOffset.UtcNow : null,
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();
        return evt;
    }

    [Fact]
    public async Task Maps_outbox_rows_back_to_their_alert_ids()
    {
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        var e1 = await Captured(User, a1, EventDisposition.Delivered);
        await Captured(User, a2, EventDisposition.Pending);
        await Captured(Guid.NewGuid(), Guid.NewGuid(), EventDisposition.Delivered);

        var rows = await _adapter.ListForAlertsAsync(User, [a1, a2, Guid.NewGuid()]);

        rows.Should().HaveCount(2);
        var r1 = rows.Single(r => r.AlertId == a1);
        r1.EventId.Should().Be(e1.Id);
        r1.Disposition.Should().Be("Delivered");
        r1.Kind.Should().Be("NewsCluster");
        r1.DeliveredAt.Should().NotBeNull();
        rows.Single(r => r.AlertId == a2).Disposition.Should().Be("Pending");
    }

    [Fact]
    public async Task Find_returns_only_the_users_own_event()
    {
        var mine = await Captured(User, Guid.NewGuid(), EventDisposition.Delivered);
        var theirs = await Captured(Guid.NewGuid(), Guid.NewGuid(), EventDisposition.Delivered);

        (await _adapter.FindAsync(User, mine.Id))!.EventId.Should().Be(mine.Id);
        (await _adapter.FindAsync(User, theirs.Id)).Should().BeNull();
        (await _adapter.FindAsync(User, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Empty_alert_set_reads_nothing()
        => (await _adapter.ListForAlertsAsync(User, [])).Should().BeEmpty();

    public void Dispose() => _db.Dispose();
}

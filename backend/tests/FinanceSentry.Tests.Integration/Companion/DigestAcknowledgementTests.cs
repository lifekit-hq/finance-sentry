namespace FinanceSentry.Tests.Integration.Companion;

using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Queries;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The digest pull followed by the agent's acknowledgement: held-for-digest events are delivered once and do not
/// resurface. The acknowledgement is a set-based <c>ExecuteUpdateAsync</c>, which the InMemory provider cannot run,
/// so these run on real Postgres; the pull-only digest semantics stay in <c>DigestConsolidationTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DigestAcknowledgementTests : IAsyncLifetime
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateDb();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private sealed class DigestModeSettings : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = NotificationMode.Digest });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class PassThroughReconciler : ISyncFailureReconciler
    {
        public Task<IReadOnlyList<CompanionEvent>> ReconcileAsync(
            IReadOnlyList<CompanionEvent> events, CancellationToken ct = default)
            => Task.FromResult(events);
    }

    private static GetPendingCompanionEventsQueryHandler NewHandler(ICompanionEventRepository events)
        => new(events, new PassThroughReconciler(), new DigestModeSettings(), Options.Create(new CompanionOptions()),
            NullLogger<GetPendingCompanionEventsQueryHandler>.Instance);

    // The pull and the ack run as User.
    private CompanionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(User));

    private static CompanionEvent Held(Guid user, string key) => new()
    {
        UserId = user, Kind = CompanionEventKind.Opportunity, Subject = "X", Severity = "info",
        Summary = "held", DedupKey = $"{key}:{Guid.NewGuid():N}", Disposition = EventDisposition.HeldForDigest,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    [DockerRequiredFact]
    public async Task Demoted_event_reaches_the_operator_exactly_once_in_the_digest()
    {
        await using var db = CreateDb();
        db.Events.Add(Held(User, "a"));
        await db.SaveChangesAsync();
        var events = new CompanionEventRepository(db);
        var handler = NewHandler(events);

        // Realtime/scan pulls (even passing the flag) never see it.
        (await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, false), default)).Events.Should().BeEmpty();
        (await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true), default)).Events.Should().BeEmpty();

        // The digest pull delivers it, then acks.
        var digest = await handler.Handle(
            new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);
        digest.Events.Should().ContainSingle();
        await new AcknowledgeCompanionEventsCommandHandler(events)
            .Handle(new AcknowledgeCompanionEventsCommand(User, [.. digest.Events.Select(e => e.Id)]), default);

        // Nothing resurfaces: not in a repeat digest, not in a realtime pull, not held for the next digest wake.
        (await handler.Handle(
            new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default)).Events.Should().BeEmpty();
        (await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, false), default)).Events.Should().BeEmpty();
        (await events.ListHeldForDigestUnscopedAsync(User)).Should().BeEmpty();
    }


    [DockerRequiredFact]
    public async Task Digest_wake_pull_with_the_wake_reason_delivers_held_events_once()
    {
        await using var db = CreateDb();
        db.Events.AddRange(Held(User, "a"), Held(User, "b"));
        await db.SaveChangesAsync();
        var events = new CompanionEventRepository(db);
        var handler = NewHandler(events);

        var digest = await handler.Handle(
            new GetPendingCompanionEventsQuery(
                User, 25, true, WebhookAgentWakeDispatcher.DigestHeldOverrideReason), default);

        digest.Events.Should().HaveCount(2);
        digest.Note.Should().BeNull();
        await new AcknowledgeCompanionEventsCommandHandler(events)
            .Handle(new AcknowledgeCompanionEventsCommand(User, [.. digest.Events.Select(e => e.Id)]), default);

        var repeat = await handler.Handle(
            new GetPendingCompanionEventsQuery(
                User, 25, true, WebhookAgentWakeDispatcher.DigestHeldOverrideReason), default);
        repeat.Events.Should().BeEmpty();
        (await events.ListHeldForDigestUnscopedAsync(User)).Should().BeEmpty();
    }


    [DockerRequiredFact]
    public async Task Acked_digest_events_do_not_repeat()
    {
        await using var db = CreateDb();
        db.Events.AddRange(Held(User, "a"), Held(User, "b"));
        await db.SaveChangesAsync();
        var events = new CompanionEventRepository(db);
        var handler = NewHandler(events);

        var first = await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);
        var ids = first.Events.Select(e => e.Id).ToList();
        await new AcknowledgeCompanionEventsCommandHandler(events)
            .Handle(new AcknowledgeCompanionEventsCommand(User, ids), default);

        var second = await handler.Handle(
            new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);
        second.Events.Should().BeEmpty("acked events are Delivered and don't resurface");
    }
}

namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Queries;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Digest consolidation semantics (feature 031, US3, T036): held-for-digest events are collected once,
/// excluded from the normal pull, and don't repeat after the agent acks. Empty → nothing.
/// </summary>
public sealed class DigestConsolidationTests
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Other = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private sealed class DigestModeSettings : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = NotificationMode.Digest });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CompanionNotificationSetting>> ListByModeAsync(
            NotificationMode mode, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionNotificationSetting>>([]);
    }

    private sealed class RecordingLogger : ILogger<GetPendingCompanionEventsQueryHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private static GetPendingCompanionEventsQueryHandler NewHandler(
        ICompanionEventRepository events, ILogger<GetPendingCompanionEventsQueryHandler>? logger = null)
        => new(events, new DigestModeSettings(), logger ?? NullLogger<GetPendingCompanionEventsQueryHandler>.Instance);

    private static CompanionDbContext NewDb() => new(
        new DbContextOptionsBuilder<CompanionDbContext>()
            .UseInMemoryDatabase($"digest-{Guid.NewGuid():N}").Options,
        // The in-memory provider applies the Owner query filter too; the pull and the ack run as User.
        new FixedCurrentUser(User));

    private static CompanionEvent Held(Guid user, string key) => new()
    {
        UserId = user, Kind = CompanionEventKind.Opportunity, Subject = "X", Severity = "info",
        Summary = "held", DedupKey = key, Disposition = EventDisposition.HeldForDigest,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Held_events_are_pulled_only_with_flag_and_reason_and_not_across_users()
    {
        await using var db = NewDb();
        db.Events.AddRange(Held(User, "a"), Held(User, "b"), Held(Other, "c"));
        await db.SaveChangesAsync();
        var events = new CompanionEventRepository(db);
        var handler = NewHandler(events);

        var withoutFlag = await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, false), default);
        withoutFlag.Events.Should().BeEmpty("held-for-digest is excluded from the normal pull");

        var flagWithoutReason = await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true), default);
        flagWithoutReason.Events.Should().BeEmpty("the flag alone cannot defeat demotion");

        var blankReason = await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true, "  "), default);
        blankReason.Events.Should().BeEmpty("a blank reason is not an explicit reason");

        var withReason = await handler.Handle(
            new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);
        withReason.Events.Should().HaveCount(2, "only this user's held events");
    }

    [Fact]
    public async Task Override_and_refused_attempts_are_logged()
    {
        await using var db = NewDb();
        db.Events.Add(Held(User, "a"));
        await db.SaveChangesAsync();
        var logger = new RecordingLogger();
        var handler = NewHandler(new CompanionEventRepository(db), logger);

        await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true), default);
        await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);

        logger.Messages.Should().HaveCount(2);
        logger.Messages[0].Should().Contain("without an override reason");
        logger.Messages[1].Should().Contain("daily digest");
    }

    [Fact]
    public async Task Demoted_event_reaches_the_operator_exactly_once_in_the_digest()
    {
        await using var db = NewDb();
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

    [Fact]
    public async Task Refused_override_is_distinguishable_from_no_events()
    {
        await using var db = NewDb();
        db.Events.Add(Held(User, "a"));
        await db.SaveChangesAsync();
        var handler = NewHandler(new CompanionEventRepository(db));

        var refused = await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, true), default);
        var plain = await handler.Handle(new GetPendingCompanionEventsQuery(User, 25, false), default);
        var honoured = await handler.Handle(
            new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);

        refused.Events.Should().BeEmpty();
        refused.Note.Should().Be(GetPendingCompanionEventsQueryHandler.HeldWithheldNote);
        plain.Note.Should().BeNull();
        honoured.Note.Should().BeNull();
    }

    [Fact]
    public async Task Digest_wake_pull_with_the_wake_reason_delivers_held_events_once()
    {
        await using var db = NewDb();
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

    [Fact]
    public async Task Acked_digest_events_do_not_repeat()
    {
        await using var db = NewDb();
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

    [Fact]
    public async Task No_held_events_yields_nothing()
    {
        await using var db = NewDb();
        var handler = NewHandler(new CompanionEventRepository(db));

        var result = await handler.Handle(
            new GetPendingCompanionEventsQuery(User, 25, true, "daily digest"), default);

        result.Events.Should().BeEmpty();
    }
}

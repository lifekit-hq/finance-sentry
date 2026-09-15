namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Issue #626: <c>save_thesis</c> errored on every write. These tests drive the production write
/// path — <see cref="SaveThesisCommandHandler"/> over the real <see cref="ThesisRepository"/> and
/// the SQLite store of <see cref="ThesisSqliteFixture"/> — with the payload the issue reported, and
/// read the row back on a separate context so nothing is served from the change tracker.
///
/// The create/update pair is the regression the issue asks for. The journal cases cover the defect
/// the write path actually carried: the Created event is appended *after* the thesis is committed,
/// so an exception from the recorder failed a call whose thesis was already saved — and each retry
/// of a create writes another row, because a create carries a fresh id.
/// </summary>
public sealed class SaveThesisWritePathTests
{
    private const string Ticker = "XRP";

    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>The stripped payload issue #626 reproduced with, verbatim in shape.</summary>
    private static SaveThesisCommand MinimalPayload(Guid? id = null) => new(
        UserId,
        id,
        Ticker,
        "Watch thesis: hold only while the regulatory path stays open.",
        KeyDataPoints: [new ThesisDataPoint("Hand-stated average cost USD", "greaterThan", 2.09m, "user 2026-09-15")],
        Catalysts: [new ThesisCatalyst(new DateOnly(2026, 12, 31), "Deadline: CLARITY vote or spot ETF, else exit.")],
        InvalidationTriggers: [new ThesisInvalidationTrigger("price_drawdown", "greaterThan", 0.45m)],
        EntryPrice: 2.09m);

    private static SaveThesisCommandHandler HandlerOver(
        ThesisSqliteFixture.ThesisOnlySqliteContext ctx, RecordingThesisEventRecorder recorder) =>
        new(new ThesisRepository(ctx), recorder, NullLogger<SaveThesisCommandHandler>.Instance);

    private static async Task<IReadOnlyList<InvestmentThesis>> ReadBackAsync(ThesisSqliteFixture fixture)
    {
        // FindByTickerAsync rather than ListAsync: both are production read paths, but ListAsync
        // orders by a DateTimeOffset, which the SQLite provider refuses to translate.
        await using var readCtx = fixture.CreateContext();
        return await new ThesisRepository(readCtx).FindByTickerAsync(UserId, Ticker, CancellationToken.None);
    }

    [Fact]
    public async Task Create_PersistsTheThesis_AndReturnsTheRecord()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var recorder = new RecordingThesisEventRecorder();

        ThesisDto saved;
        await using (var ctx = fixture.CreateContext())
        {
            saved = await HandlerOver(ctx, recorder).Handle(MinimalPayload(), CancellationToken.None);
        }

        saved.Ticker.Should().Be(Ticker);
        saved.EntryPrice.Should().Be(2.09m);
        saved.InvalidationTriggers.Should().ContainSingle()
            .Which.Metric.Should().Be("price_drawdown");

        var persisted = (await ReadBackAsync(fixture)).Should().ContainSingle().Subject;
        persisted.Id.Should().Be(saved.Id, "the returned record must name the row that was written");
        persisted.EntryPrice.Should().Be(2.09m);
        persisted.KeyDataPoints.Should().ContainSingle()
            .Which.Source.Should().Be("user 2026-09-15", "the jsonb payload has to survive the round trip");
        persisted.Catalysts.Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2026, 12, 31));

        recorder.Recorded.Should().ContainSingle()
            .Which.EventType.Should().Be(ThesisEventType.Created);
    }

    [Fact]
    public async Task Update_RewritesTheSameRow_AndRecordsNoSecondCreatedEvent()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var recorder = new RecordingThesisEventRecorder();

        Guid id;
        await using (var ctx = fixture.CreateContext())
        {
            id = (await HandlerOver(ctx, recorder).Handle(MinimalPayload(), CancellationToken.None)).Id;
        }

        ThesisDto updated;
        await using (var ctx = fixture.CreateContext())
        {
            updated = await HandlerOver(ctx, recorder).Handle(
                MinimalPayload(id) with
                {
                    ThesisText = "Watch thesis: exit unless the ETF decision lands.",
                    EntryPrice = 2.40m,
                    InvalidationTriggers = [new ThesisInvalidationTrigger("price_return", "lessThan", -0.20m)],
                },
                CancellationToken.None);
        }

        updated.Id.Should().Be(id, "an update must not mint a new thesis");

        var persisted = (await ReadBackAsync(fixture)).Should().ContainSingle(
            "an update rewrites the row rather than appending a second one").Subject;
        persisted.ThesisText.Should().Be("Watch thesis: exit unless the ETF decision lands.");
        persisted.EntryPrice.Should().Be(2.40m);
        persisted.InvalidationTriggers.Should().ContainSingle()
            .Which.Metric.Should().Be("price_return");

        recorder.Recorded.Should().ContainSingle(
            "FR-001/FR-002 allow exactly one Created event per thesis, never one per update");
    }

    [Fact]
    public async Task Create_StillSucceeds_WhenTheDecisionJournalIsUnavailable()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var brokenRecorder = new RecordingThesisEventRecorder
        {
            FailWith = new InvalidOperationException("thesis_events is unavailable"),
        };

        ThesisDto saved;
        await using (var ctx = fixture.CreateContext())
        {
            saved = await HandlerOver(ctx, brokenRecorder).Handle(MinimalPayload(), CancellationToken.None);
        }

        saved.Ticker.Should().Be(Ticker);

        var persisted = (await ReadBackAsync(fixture)).Should().ContainSingle().Subject;
        persisted.Id.Should().Be(
            saved.Id,
            "the thesis is committed before the journal append, so a journal failure must not be "
            + "reported as a failed save — issue #626, where the caller retried four times and "
            + "every retry of a create mints a fresh id");
    }
}

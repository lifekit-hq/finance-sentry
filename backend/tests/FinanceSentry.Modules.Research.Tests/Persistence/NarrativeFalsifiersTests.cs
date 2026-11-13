namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Narrative falsifiers (P7): free-text kill-switches stored on the thesis beside, not inside, the closed metric
/// vocabulary. These drive the production <see cref="SaveThesisCommandHandler"/> over the real repository and the real
/// event recorder on SQLite, and read the row back on a fresh context.
/// </summary>
public sealed class NarrativeFalsifiersTests
{
    private const string Ticker = "CBRS";

    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly ThesisInvalidationTrigger Drawdown = new("price_drawdown", "greaterThan", 0.30m);

    private sealed class Harness(ThesisSqliteFixture fixture)
    {
        public InMemoryThesisEventRepository Journal { get; } = new();

        public async Task<ThesisDto> SaveAsync(
            ThesisEventOrigin origin,
            string text = "Wafer-scale inference demand.",
            Guid? id = null,
            IReadOnlyList<string>? falsifiers = null,
            string? rationale = null)
        {
            await using var ctx = fixture.CreateContext(UserId);
            var handler = new SaveThesisCommandHandler(
                new ThesisRepository(ctx), Recorder(), new ResearchWriteScope(ctx), new ThesisOriginReader(Journal),
                NullLogger<SaveThesisCommandHandler>.Instance);
            return await handler.Handle(
                new SaveThesisCommand(
                    UserId, id, Ticker, text, [], [], [Drawdown],
                    Origin: origin, Rationale: rationale, NarrativeFalsifiers: falsifiers),
                CancellationToken.None);
        }

        public async Task<RevertThesisRevisionResult> RevertAsync(Guid eventId, ThesisEventOrigin origin)
        {
            await using var ctx = fixture.CreateContext(UserId);
            var handler = new RevertThesisRevisionCommandHandler(new ThesisRepository(ctx), Journal, Recorder(), new ResearchWriteScope(ctx));
            return await handler.Handle(new RevertThesisRevisionCommand(UserId, eventId, null, origin), CancellationToken.None);
        }

        public async Task<InvestmentThesis> ReadAsync(Guid id)
        {
            await using var ctx = fixture.CreateContext(UserId);
            return (await new ThesisRepository(ctx).FindAsync(UserId, id, CancellationToken.None))!;
        }

        private ThesisEventRecorder Recorder()
        {
            var quotes = new Mock<IMarketDataService>();
            quotes.Setup(q => q.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>());
            return new ThesisEventRecorder(Journal, quotes.Object, NullLogger<ThesisEventRecorder>.Instance);
        }
    }

    [Fact]
    public async Task Save_RoundTrips_TheFalsifiers_ThroughTheStoreAndTheReturnedDto()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        var saved = await h.SaveAsync(
            ThesisEventOrigin.User, falsifiers: ["Loses the OpenAI contract", "Cerebras IPO withdrawn"]);

        saved.NarrativeFalsifiers.Should().Equal("Loses the OpenAI contract", "Cerebras IPO withdrawn");
        (await h.ReadAsync(saved.Id)).NarrativeFalsifiers.Should().Equal("Loses the OpenAI contract", "Cerebras IPO withdrawn");
    }

    [Fact]
    public async Task Save_TrimsDropsBlanksAndCollapsesRepeats()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        var saved = await h.SaveAsync(
            ThesisEventOrigin.User, falsifiers: ["  Loses the contract ", "", "   ", "loses the contract", "Second"]);

        saved.NarrativeFalsifiers.Should().Equal("Loses the contract", "Second");
    }

    [Fact]
    public async Task Save_RefusesAnOverlongFalsifier_InsteadOfCuttingIt()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        var act = () => h.SaveAsync(ThesisEventOrigin.User, falsifiers: [new string('x', 501)]);

        (await act.Should().ThrowAsync<InvalidNarrativeFalsifierException>()).Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Save_RefusesMoreThanTwentyFalsifiers()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        var act = () => h.SaveAsync(ThesisEventOrigin.User, falsifiers: [.. Enumerable.Range(0, 21).Select(i => $"f{i}")]);

        await act.Should().ThrowAsync<InvalidNarrativeFalsifierException>();
    }

    [Fact]
    public async Task A_thesis_saved_without_the_field_has_an_empty_list_and_an_unaffected_audit()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        var saved = await h.SaveAsync(ThesisEventOrigin.User);

        saved.NarrativeFalsifiers.Should().BeEmpty();
        (await h.ReadAsync(saved.Id)).NarrativeFalsifiers.Should().BeEmpty();
        h.Journal.Rows.Should().ContainSingle(e => e.EventType == ThesisEventType.Created);
    }

    [Fact]
    public async Task Update_WithoutTheField_LeavesTheStoredFalsifiers_AndAppendsNoEvent()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, falsifiers: ["Loses the contract"]);

        var again = await h.SaveAsync(ThesisEventOrigin.Ledger, id: created.Id);

        again.NarrativeFalsifiers.Should().Equal("Loses the contract");
        h.Journal.Rows.Should().ContainSingle(e => e.EventType == ThesisEventType.Created);
    }

    [Fact]
    public async Task Update_WithAnEmptyList_ClearsThem()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, falsifiers: ["Loses the contract"]);

        await h.SaveAsync(ThesisEventOrigin.User, id: created.Id, falsifiers: []);

        (await h.ReadAsync(created.Id)).NarrativeFalsifiers.Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerCreate_AuditsTheFalsifiersOnTheNewVersion()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);

        await h.SaveAsync(ThesisEventOrigin.Ledger, falsifiers: ["Loses the contract"], rationale: "Initial case");

        var created = h.Journal.Rows.Should().ContainSingle().Subject;
        created.Origin.Should().Be(ThesisEventOrigin.Ledger);
        created.Audit!.New!.NarrativeFalsifiers.Should().Equal("Loses the contract");
    }

    [Fact]
    public async Task LedgerRevision_ThatOnlyChangesFalsifiers_IsJournaledWithPriorAndNewLists()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, falsifiers: ["Loses the contract"]);

        await h.SaveAsync(
            ThesisEventOrigin.Ledger, id: created.Id,
            falsifiers: ["Loses the contract", "CFO departs"], rationale: "Added after the call");

        var revision = h.Journal.Rows.Should().Contain(e => e.EventType == ThesisEventType.Revised).Subject;
        revision.Origin.Should().Be(ThesisEventOrigin.Ledger);
        revision.Audit!.Prior!.NarrativeFalsifiers.Should().Equal("Loses the contract");
        revision.Audit.New!.NarrativeFalsifiers.Should().Equal("Loses the contract", "CFO departs");
        revision.Audit.Rationale.Should().Be("Added after the call");
    }

    [Fact]
    public async Task Revert_RestoresThePriorFalsifiers()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var h = new Harness(fixture);
        var created = await h.SaveAsync(ThesisEventOrigin.User, falsifiers: ["Loses the contract"]);
        await h.SaveAsync(ThesisEventOrigin.Ledger, id: created.Id, falsifiers: ["Something else"]);
        var revision = h.Journal.Rows.Single(e => e.EventType == ThesisEventType.Revised);

        var result = await h.RevertAsync(revision.Id, ThesisEventOrigin.User);

        result.Thesis!.NarrativeFalsifiers.Should().Equal("Loses the contract");
        (await h.ReadAsync(created.Id)).NarrativeFalsifiers.Should().Equal("Loses the contract");
    }

    [Fact]
    public void An_audit_written_before_the_field_existed_compares_equal_to_an_empty_list_and_restores_to_none()
    {
        var legacy = new ThesisVersion("NVDA", "Case", null, [], [], [Drawdown]);
        var current = legacy with { NarrativeFalsifiers = [] };

        legacy.SameContentAs(current).Should().BeTrue();
        var thesis = new InvestmentThesis { NarrativeFalsifiers = ["stale"] };
        legacy.ApplyTo(thesis);
        thesis.NarrativeFalsifiers.Should().BeEmpty();
    }
}

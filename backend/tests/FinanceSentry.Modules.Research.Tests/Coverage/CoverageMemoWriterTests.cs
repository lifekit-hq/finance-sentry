namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Stage 6, the research memo on coverage entrants: LogOnly by default, one call per ranked entrant, capped per day and by a
/// rolling weekly budget, never for the owner's own held or watchlisted names.
/// </summary>
public sealed class CoverageMemoWriterTests
{
    private const string GoodReply = """
        {"business":"Sells memory.","whyNow":"Two Buy readings.","falsifiers":[{"kind":"narrative","statement":"HBM share loss"}],
         "crowding":"Normal.","correlationToBook":"0.56 to DRAM.","verdict":"Watch","caveats":""}
        """;

    private readonly CoverageHarness _h = new();
    private readonly ScriptedModel _model = new();
    private readonly Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>> _scorecard = new();

    public CoverageMemoWriterTests()
    {
        _h.Options.MemoMode = CoverageMemoMode.LogOnly;
        _scorecard.Setup(s => s.Handle(It.IsAny<GetScorecardQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetScorecardQuery q, CancellationToken _) => Card(q.Symbol));
    }

    [Fact]
    public async Task ModeOff_MakesNoCall_EvenWithEntrantsAndAConfiguredModel()
    {
        _h.Options.MemoMode = CoverageMemoMode.Off;
        await EntrantAsync("MU");

        var summary = await Writer().WriteAllAsync(CoverageHarness.Day1);

        summary.Should().Be(CoverageMemoRunSummary.Empty);
        _model.Calls.Should().BeEmpty();
        (await Memos()).Should().BeEmpty();
    }

    [Fact]
    public async Task ModeLogOnly_IsTheDefaultOfTheOptions()
        => new CoverageOptions().MemoMode.Should().Be(CoverageMemoMode.LogOnly);

    [Fact]
    public async Task NoModelOrNoKey_MakesNoCall()
    {
        await EntrantAsync("MU");

        (await Writer(withModel: false).WriteAllAsync(CoverageHarness.Day1)).Should().Be(CoverageMemoRunSummary.Empty);
        _model.Configured = false;
        (await Writer().WriteAllAsync(CoverageHarness.Day1)).Should().Be(CoverageMemoRunSummary.Empty);

        _model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task WritesOneStructuredMemoPerRankedEntrant_PricedAtTheListPrice_AndNotTwice()
    {
        var entry = await EntrantAsync("MU");
        _model.Reply = new MemoModelReply(GoodReply, "claude-sonnet-5", 10_000, 1_000);

        var first = await Writer().WriteAllAsync(CoverageHarness.Day1);
        var second = await Writer().WriteAllAsync(CoverageHarness.Day1.AddHours(1));

        first.Written.Should().Be(1);
        second.Should().Be(CoverageMemoRunSummary.Empty);
        _model.Calls.Should().ContainSingle();
        var memo = (await Memos()).Single();
        memo.JournalEntryId.Should().Be(entry.Id);
        memo.Ticker.Should().Be("MU");
        memo.Status.Should().Be(CoverageMemoStatus.Structured);
        memo.Verdict.Should().Be("Watch");
        memo.CostUsd.Should().Be(0.03m, "10,000 input tokens at $2 per million plus 1,000 output tokens at $10 per million");
        CoverageMemoParser.Deserialize(memo.Body)!.Business.Should().Be("Sells memory.");
    }

    [Fact]
    public async Task TheCallCarriesTheSystemPrompt_TheEvidenceAndTheConfiguredLimits()
    {
        _h.Options.MemoModelId = "claude-test";
        _h.Options.MemoMaxOutputTokens = 1234;
        await EntrantAsync("MU");
        _h.Db.CoverageEvents.Add(new CoverageEvent { UserId = CoverageHarness.UserId, Ticker = "MU", Source = "filing", Reference = "r1", Summary = "10-Q filed" });
        await _h.Db.SaveChangesAsync();

        await Writer().WriteAllAsync(CoverageHarness.Day1);

        var call = _model.Calls.Single();
        call.System.Should().Be(CoverageMemoPrompt.System);
        call.User.Should().Contain("MU").And.Contain("10-Q filed");
        call.MaxOutputTokens.Should().Be(1234);
        call.ModelId.Should().Be("claude-test");
    }

    [Fact]
    public async Task HeldWatchlistedAndRemovedNames_AreNotEntrants()
    {
        await EntrantAsync("HELD", CoverageReason.Held, CoverageRuleIds.Held);
        await EntrantAsync("WATCH", CoverageReason.Watchlist, CoverageRuleIds.Watchlist);
        await EntrantAsync("GONE", member: false);
        await EntrantAsync("OLD", at: CoverageHarness.Day1.AddDays(-30));

        var summary = await Writer().WriteAllAsync(CoverageHarness.Day1);

        summary.Should().Be(CoverageMemoRunSummary.Empty);
        _model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheDailyCap_LimitsCallsAcrossRuns_AndTheBestRankedEntrantsGoFirst()
    {
        _h.Options.MaxMemosPerDay = 2;
        await EntrantAsync("LOW", rank: 10);
        await EntrantAsync("TOP", rank: 90);
        await EntrantAsync("MID", rank: 50);

        var first = await Writer().WriteAllAsync(CoverageHarness.Day1);
        var sameDay = await Writer().WriteAllAsync(CoverageHarness.Day1.AddHours(2));
        var nextDay = await Writer().WriteAllAsync(CoverageHarness.Day1.AddDays(1));

        first.Written.Should().Be(2);
        first.DeferredByDailyCap.Should().Be(1);
        sameDay.Written.Should().Be(0);
        sameDay.DeferredByDailyCap.Should().Be(1);
        nextDay.Written.Should().Be(1);
        (await Memos()).OrderBy(m => m.CreatedAt).Select(m => m.Ticker).Should().Equal("TOP", "MID", "LOW");
    }

    [Fact]
    public async Task TheWeeklyBudget_StopsTheRun_BeforeACallThatCouldOverspend()
    {
        _h.Options.MemoWeeklyBudgetUsd = 0.05m;
        _h.Options.MemoMaxOutputTokens = 2000;
        _h.Options.MaxMemosPerDay = 10;
        _model.Reply = new MemoModelReply(GoodReply, "m", 1_000, 2_000);
        await EntrantAsync("AAA", rank: 3);
        await EntrantAsync("BBB", rank: 2);
        await EntrantAsync("CCC", rank: 1);

        var summary = await Writer().WriteAllAsync(CoverageHarness.Day1);

        // Worst case per call is about $0.02 + a prompt of a few hundred tokens; the first call costs $0.022 and records it.
        summary.Written.Should().BeGreaterThan(0).And.BeLessThan(3);
        summary.DeferredByBudget.Should().BeGreaterThan(0);
        (await Memos()).Sum(m => m.CostUsd).Should().BeLessThanOrEqualTo(0.05m);
    }

    [Fact]
    public async Task ASpentWeek_BlocksEveryCall_AndAnOldMemoDoesNotCountAgainstTheWeek()
    {
        _h.Options.MemoWeeklyBudgetUsd = 1m;
        await EntrantAsync("MU");
        _h.Db.CoverageMemos.Add(Spent(CoverageHarness.Day1.AddDays(-2), 0.99m));
        await _h.Db.SaveChangesAsync();

        var blocked = await Writer().WriteAllAsync(CoverageHarness.Day1);

        blocked.Written.Should().Be(0);
        blocked.DeferredByBudget.Should().Be(1);
        _model.Calls.Should().BeEmpty();

        var later = await Writer().WriteAllAsync(CoverageHarness.Day1.AddDays(6));
        later.Written.Should().Be(1, "the $0.99 memo is more than seven days old by then");
    }

    [Fact]
    public async Task AReplyThatIsNotTheMemoShape_IsKeptAsRawText_AndPaidFor()
    {
        _model.Reply = new MemoModelReply("I cannot say.", "m", 1_000, 100);
        await EntrantAsync("MU");

        var summary = await Writer().WriteAllAsync(CoverageHarness.Day1);

        summary.Should().Be(new CoverageMemoRunSummary(1, 1, 0, 0, 0));
        var memo = (await Memos()).Single();
        memo.Status.Should().Be(CoverageMemoStatus.Unstructured);
        memo.Verdict.Should().BeNull();
        memo.Body.Should().Be("I cannot say.");
        memo.CostUsd.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task AProviderFailure_StopsTheRunAndFailsTheJob_WithoutLosingEarlierMemos()
    {
        _model.FailOn = "BBB";
        await EntrantAsync("AAA", rank: 9);
        await EntrantAsync("BBB", rank: 5);
        await EntrantAsync("CCC", rank: 1);

        var run = () => Writer().WriteAllAsync(CoverageHarness.Day1);

        await run.Should().ThrowAsync<InvalidOperationException>();
        (await Memos()).Select(m => m.Ticker).Should().Equal("AAA");
        _model.Calls.Select(c => c.User.Contains("BBB")).Should().Equal(false, true);
    }

    [Fact]
    public async Task AFailedEntrant_IsRetriedTheNextNight()
    {
        _model.FailOn = "MU";
        await EntrantAsync("MU");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer().WriteAllAsync(CoverageHarness.Day1));
        _model.FailOn = null;

        var retry = await Writer().WriteAllAsync(CoverageHarness.Day1.AddDays(1));

        retry.Written.Should().Be(1);
    }

    [Fact]
    public async Task AnUnreadableScorecard_SpendsNothing_AndTheEntrantWaits()
    {
        _scorecard.Setup(s => s.Handle(It.IsAny<GetScorecardQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));
        await EntrantAsync("MU");

        var summary = await Writer().WriteAllAsync(CoverageHarness.Day1);

        summary.Should().Be(CoverageMemoRunSummary.Empty);
        _model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Entrants_AreEachPersonsOwn_AndMemosAreStoredUnderTheirUser()
    {
        var other = Guid.NewGuid();
        await EntrantAsync("MU");
        var journal = new CoverageJournalEntry
        {
            UserId = other, Ticker = "MU", Action = CoverageAction.Added, Rule = CoverageRuleIds.BuyTwice,
            Reason = CoverageReason.Ledger, At = CoverageHarness.Day1.AddDays(-1),
        };
        _h.Db.CoverageMembers.Add(new CoverageMember { UserId = other, Ticker = "MU", AddedReason = CoverageReason.Ledger });
        _h.Db.CoverageJournal.Add(journal);
        await _h.Db.SaveChangesAsync();

        var summary = await Writer().WriteAllAsync(CoverageHarness.Day1);

        summary.Written.Should().Be(2);
        (await Memos()).Select(m => m.UserId).Should().BeEquivalentTo([CoverageHarness.UserId, other]);
    }

    private CoverageMemoWriter Writer(bool withModel = true) => new(
        new CoverageMemoRepository(_h.Db),
        _h.Coverage,
        _scorecard.Object,
        Options.Create(_h.Options),
        NullLogger<CoverageMemoWriter>.Instance,
        withModel ? _model : null);

    private async Task<CoverageJournalEntry> EntrantAsync(
        string ticker,
        CoverageReason reason = CoverageReason.Screen,
        string rule = CoverageRuleIds.BuyTwice,
        bool member = true,
        int? rank = 50,
        DateTimeOffset? at = null)
    {
        var entry = new CoverageJournalEntry
        {
            UserId = CoverageHarness.UserId,
            Ticker = ticker,
            Action = CoverageAction.Added,
            Rule = rule,
            Reason = reason,
            Detail = "Scored Buy on 2 days.",
            At = at ?? CoverageHarness.Day1.AddDays(-1),
            RankScore = rank,
        };
        _h.Db.CoverageJournal.Add(entry);
        if (member)
        {
            _h.Db.CoverageMembers.Add(new CoverageMember { UserId = CoverageHarness.UserId, Ticker = ticker, AddedReason = reason });
        }

        await _h.Db.SaveChangesAsync();
        _h.Db.ChangeTracker.Clear();
        return entry;
    }

    private static CoverageMemo Spent(DateTimeOffset at, decimal cost) => new()
    {
        UserId = CoverageHarness.UserId, Ticker = "OLD", JournalEntryId = Guid.NewGuid(), CreatedAt = at,
        ModelId = "m", CostUsd = cost, Body = "x",
    };

    private Task<List<CoverageMemo>> Memos()
        => _h.Db.CoverageMemos.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync();

    private static ScorecardResult Card(string symbol)
    {
        var verdict = VerdictRules.Evaluate(
            new VerdictInputs(80, 72, CrowdingClass.Normal, false, 15m), new VerdictOptions());
        return new ScorecardResult(
            symbol, DateTimeOffset.UtcNow, 1,
            new ScorecardStructureAxis(80, 80, 0, [], null, null, null, new Dictionary<int, decimal?>(), new Dictionary<int, decimal?>(), null, null, null, null, null, null, []),
            new ScorecardFundamentalsAxis(72, null, null, null, null, null, []),
            new ScorecardCrowdingAxis("Normal", null, null, 0m, 0m, 0m, null),
            ScorecardValuationAxis.Unavailable,
            VerdictDto.From(verdict, VerdictDto.AdjustedBasis));
    }

    private sealed class ScriptedModel : IMemoLanguageModel
    {
        public bool Configured { get; set; } = true;

        public MemoModelReply Reply { get; set; } = new(GoodReply, "claude-sonnet-5", 2_000, 500);

        public string? FailOn { get; set; }

        public List<MemoModelRequest> Calls { get; } = [];

        public bool IsConfigured => Configured;

        public Task<MemoModelReply> CompleteAsync(MemoModelRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            return FailOn is not null && request.User.Contains($"\"ticker\":\"{FailOn}\"")
                ? throw new HttpRequestException("provider down")
                : Task.FromResult(Reply);
        }
    }
}

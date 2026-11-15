namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>The reads of stage 6 and the held soft cap (owner call D2): <c>list_coverage</c> and <c>explain_coverage</c>.</summary>
public sealed class CoverageMemoViewsTests
{
    private static readonly DateTimeOffset Day1 = CoverageHarness.Day1;

    private readonly CoverageHarness _h = new();
    private readonly MovableClock _clock = new(Day1);

    [Fact]
    public async Task List_ReportsHeldNamesAgainstTheSoftCap_WithoutBlockingAnything()
    {
        for (var i = 0; i < 13; i++)
        {
            _h.Hold($"H{i:00}");
        }

        _h.Hold("BTC", type: "CRYPTO");
        await _h.RunAsync(Day1);

        var view = await ListAsync();

        view.HeldCount.Should().Be(13, "only held equities count toward the book's name limit");
        view.HeldSoftCap.Should().Be(12);
        view.HeldOverSoftCap.Should().BeTrue();
        view.Count.Should().Be(13, "held names always join coverage, over the cap or not");
    }

    [Fact]
    public async Task List_IsWithinTheSoftCap_AtTwelveHeldNames()
    {
        for (var i = 0; i < 12; i++)
        {
            _h.Hold($"H{i:00}");
        }

        await _h.RunAsync(Day1);

        (await ListAsync()).HeldOverSoftCap.Should().BeFalse();
    }

    [Fact]
    public async Task List_MarksTheMembersThatHaveAMemo()
    {
        await _h.MemberAsync("MU");
        await _h.MemberAsync("AMD");
        _h.Db.CoverageMemos.Add(Memo("MU", Day1.AddDays(-2)));
        _h.Db.CoverageMemos.Add(Memo("MU", Day1.AddDays(-1)));
        await _h.Db.SaveChangesAsync();

        var members = (await ListAsync()).Members;

        members.Single(m => m.Ticker == "MU").LatestMemoAt.Should().Be(Day1.AddDays(-1));
        members.Single(m => m.Ticker == "AMD").LatestMemoAt.Should().BeNull();
    }

    [Fact]
    public async Task Explain_CarriesTheNewestStructuredMemo_AsPaperOnly()
    {
        await _h.MemberAsync("MU");
        _h.Db.CoverageMemos.Add(Memo("MU", Day1.AddDays(-3), body: "older"));
        var content = new CoverageMemoContent(
            "Sells memory.", "Two Buy readings.", [new CoverageMemoFalsifier("narrative", "HBM share loss")],
            "Normal.", "0.56 to DRAM.", "Watch", string.Empty);
        _h.Db.CoverageMemos.Add(Memo("MU", Day1.AddDays(-1), body: CoverageMemoParser.Serialize(content), verdict: "Watch"));
        await _h.Db.SaveChangesAsync();

        var explanation = await ExplainAsync("mu");

        explanation.Memo.Should().NotBeNull();
        explanation.Memo!.At.Should().Be(Day1.AddDays(-1));
        explanation.Memo.Verdict.Should().Be("Watch");
        explanation.Memo.Content.Should().BeEquivalentTo(content);
        explanation.Memo.RawText.Should().BeNull();
        explanation.Memo.PaperOnly.Should().BeTrue();
        explanation.Member!.LatestMemoAt.Should().Be(Day1.AddDays(-1));
    }

    [Fact]
    public async Task Explain_ShowsAnUnstructuredMemoAsRawText()
    {
        await _h.MemberAsync("MU");
        _h.Db.CoverageMemos.Add(Memo("MU", Day1.AddDays(-1), body: "I cannot say.", status: CoverageMemoStatus.Unstructured));
        await _h.Db.SaveChangesAsync();

        var memo = (await ExplainAsync("MU")).Memo;

        memo!.Content.Should().BeNull();
        memo.RawText.Should().Be("I cannot say.");
        memo.Status.Should().Be("Unstructured");
    }

    [Fact]
    public async Task Explain_HasNoMemo_ForANameWithoutOne()
    {
        await _h.MemberAsync("MU");

        (await ExplainAsync("MU")).Memo.Should().BeNull();
    }

    private Task<CoverageListView> ListAsync()
        => new ListCoverageQueryHandler(
                _h.Coverage, _h.Snapshots, new CoverageMemoRepository(_h.Db), _clock, Options.Create(_h.Options))
            .Handle(new ListCoverageQuery(CoverageHarness.UserId), CancellationToken.None);

    private Task<CoverageExplanation> ExplainAsync(string ticker)
        => new ExplainCoverageQueryHandler(_h.Explainer, new CoverageMemoRepository(_h.Db), _clock)
            .Handle(new ExplainCoverageQuery(CoverageHarness.UserId, ticker), CancellationToken.None);

    private static CoverageMemo Memo(
        string ticker, DateTimeOffset at, string body = "{}", string? verdict = null,
        CoverageMemoStatus status = CoverageMemoStatus.Structured)
        => new()
        {
            UserId = CoverageHarness.UserId, Ticker = ticker, JournalEntryId = Guid.NewGuid(), CreatedAt = at,
            ModelId = "claude-sonnet-5", CostUsd = 0.03m, Status = status, Verdict = verdict, Body = body,
        };
}

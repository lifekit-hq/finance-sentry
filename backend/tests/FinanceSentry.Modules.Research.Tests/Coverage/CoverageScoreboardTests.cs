namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FinanceSentry.Modules.Research.Tests.Recommendations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>The scoreboard's coverage block: the list graded against the S&amp;P 500 from each member's add date.</summary>
public sealed class CoverageScoreboardTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Day0 = new(2026, 1, 5, 1, 0, 0, TimeSpan.Zero);

    private readonly List<CoverageMember> _members = [];
    private readonly List<CoverageJournalEntry> _journal = [];
    private readonly FakeCandidateRepository _candidates = new();
    private readonly Dictionary<string, List<AdjustedClose>> _bars = new(StringComparer.OrdinalIgnoreCase);

    private async Task<CoverageVsBenchmarkDto> Block(DateTimeOffset asOf)
    {
        var coverage = new Mock<ICoverageRepository>();
        coverage.Setup(c => c.ListMembersUnscopedAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(() => _members);
        coverage.Setup(c => c.ListJournalUnscopedAsync(User, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _journal);
        var closes = new Mock<IAdjustedCloseReader>();
        closes.Setup(c => c.GetSinceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, DateOnly since, CancellationToken _) =>
                (IReadOnlyList<AdjustedClose>)(_bars.TryGetValue(t, out var s) ? s.Where(b => b.Date >= since).ToList() : []));
        var events = new Mock<IThesisEventRepository>();
        events.Setup(e => e.ListUnscopedAsync(User, null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var handler = new GetLedgerScoreboardQueryHandler(
            new InMemoryRecommendationRepository(), _candidates, events.Object, new ThesisPerformanceCalculator(),
            Options.Create(new FrictionConfig()), coverage.Object, closes.Object);
        var board = await handler.Handle(new GetLedgerScoreboardQuery(User, asOf), CancellationToken.None);
        return board.Coverage!;
    }

    private void Series(string ticker, DateTimeOffset from, int days, decimal start, decimal end)
    {
        var list = new List<AdjustedClose>();
        for (var i = 0; i <= days; i++)
        {
            list.Add(new AdjustedClose(
                DateOnly.FromDateTime(from.AddDays(i).UtcDateTime), start + (end - start) * i / days));
        }

        _bars[ticker] = list;
    }

    private void Add(string ticker, DateTimeOffset at, CoverageReason reason, DateTimeOffset? removedAt = null)
    {
        _journal.Add(new CoverageJournalEntry { UserId = User, Ticker = ticker, Action = CoverageAction.Added, Reason = reason, At = at });
        if (removedAt is { } r)
        {
            _journal.Add(new CoverageJournalEntry { UserId = User, Ticker = ticker, Action = CoverageAction.Removed, Reason = reason, At = r });
        }
        else
        {
            _members.Add(new CoverageMember { UserId = User, Ticker = ticker, AddedAt = at, AddedReason = reason });
        }
    }

    private static CoverageHorizonDto Headline(CoverageVsBenchmarkDto b) => b.Horizons.Single(h => h.IsHeadline);

    [Fact]
    public async Task Empty_coverage_reads_not_enough_history_with_no_figures()
    {
        var b = await Block(Day0.AddDays(200));

        b.CurrentMembers.Should().Be(0);
        Headline(b).Overall.Status.Should().Be("NotEnoughHistory");
        Headline(b).Overall.Message.Should().Contain("not enough history yet");
        Headline(b).Overall.ExcessPct.Should().BeNull();
        Headline(b).BySource.Should().BeEmpty();
    }

    [Fact]
    public async Task Equal_weight_list_return_is_compared_with_the_benchmark_over_the_same_windows()
    {
        Add("AAA", Day0, CoverageReason.Held);
        Add("BBB", Day0, CoverageReason.Held);
        Series("AAA", Day0, 120, 100m, 130m);   // +30% at 90 days is 100 + 30 * 90/120 = 122.5 -> +22.5%
        Series("BBB", Day0, 120, 100m, 80m);    // 100 - 20 * 90/120 = 85 -> -15%
        Series("SPY", Day0, 120, 400m, 480m);   // 400 + 80 * 90/120 = 460 -> +15%

        var overall = Headline(await Block(Day0.AddDays(100))).Overall;

        overall.Graded.Should().Be(2);
        overall.ListReturnPct.Should().Be(3.75m);          // mean(22.5, -15)
        overall.BenchmarkReturnPct.Should().Be(15m);
        overall.ExcessPct.Should().Be(-11.25m);
        overall.BeatBenchmarkSharePct.Should().Be(50m);
        overall.Status.Should().Be("Graded");
        overall.TooFewToTell.Should().BeTrue();
    }

    [Fact]
    public async Task A_horizon_that_has_not_elapsed_is_reported_as_awaiting_not_graded()
    {
        Add("AAA", Day0, CoverageReason.Held);
        Series("AAA", Day0, 120, 100m, 130m);
        Series("SPY", Day0, 120, 400m, 480m);

        var b = await Block(Day0.AddDays(45));

        var ninety = Headline(b).Overall;
        ninety.Status.Should().Be("NotEnoughHistory");
        ninety.Message.Should().Be("not enough history yet");
        ninety.Awaiting.Should().Be(1);
        ninety.ListReturnPct.Should().BeNull();
        b.Horizons.Single(h => h.HorizonDays == 30).Overall.Status.Should().Be("Graded");
    }

    [Theory]
    [InlineData(CandidateSource.Scan, "scan: top-decile RS", "screen: dated catalyst", "screen: dated catalyst")]
    [InlineData(CandidateSource.User, "conviction", "screen: fundamentals", "screen: fundamentals")]
    [InlineData(CandidateSource.Scan, "screen: relative strength", "scan: top-decile RS", "screen: relative strength")]
    [InlineData(CandidateSource.Scan, "scan: top-decile RS", "scan: 63d-high breakout", "scan: top-decile RS")]
    public async Task A_screen_add_is_attributed_to_the_first_screen_tag_whatever_its_position(
        CandidateSource source, string first, string second, string expected)
    {
        Add("AAA", Day0, CoverageReason.Screen);
        Series("AAA", Day0, 120, 100m, 140m);
        Series("SPY", Day0, 120, 400m, 400m);
        _candidates.Candidates.Add(new OpportunityCandidate
        {
            UserId = User, Ticker = "AAA", Source = source, CreatedAt = Day0.AddDays(-2),
            NominationReasons = [first, second],
        });

        var bySource = Headline(await Block(Day0.AddDays(100))).BySource;

        bySource.Single(s => s.Source == "Screen").ScreenTag.Should().Be(expected);
    }

    [Fact]
    public async Task Sources_are_attributed_by_add_reason_and_screen_tag()
    {
        Add("AAA", Day0, CoverageReason.Screen);
        Add("BBB", Day0, CoverageReason.Ledger);
        Add("CCC", Day0, CoverageReason.Held);
        foreach (var t in new[] { "AAA", "BBB", "CCC" })
        {
            Series(t, Day0, 120, 100m, 100m + (t == "AAA" ? 40m : 0m));
        }

        Series("SPY", Day0, 120, 400m, 400m);
        _candidates.Candidates.Add(new OpportunityCandidate
        {
            UserId = User, Ticker = "AAA", Source = CandidateSource.Scan, CreatedAt = Day0.AddDays(-2),
            NominationReasons = ["momentum-breakout"],
        });

        var bySource = Headline(await Block(Day0.AddDays(100))).BySource;

        bySource.Should().HaveCount(3);
        var screen = bySource.Single(s => s.Source == "Screen");
        screen.ScreenTag.Should().Be("momentum-breakout");
        screen.Grade.ExcessPct.Should().Be(30m);   // 100 -> 130 at day 90, benchmark flat
        bySource.Single(s => s.Source == "Ledger").Grade.ExcessPct.Should().Be(0m);
        bySource.Single(s => s.Source == "Held").ScreenTag.Should().BeNull();
    }

    [Fact]
    public async Task A_removed_member_keeps_its_history_up_to_the_removal_date()
    {
        Add("AAA", Day0, CoverageReason.Screen, removedAt: Day0.AddDays(40));
        Series("AAA", Day0, 120, 100m, 220m);   // +1/day: removal at day 40 -> 140 -> +40%
        Series("SPY", Day0, 120, 400m, 400m);

        var overall = Headline(await Block(Day0.AddDays(100))).Overall;

        overall.Memberships.Should().Be(1);
        overall.Graded.Should().Be(1);
        overall.ThroughRemoval.Should().Be(1);
        overall.ListReturnPct.Should().Be(40m);
        overall.ExcessPct.Should().Be(40m);
    }

    [Fact]
    public async Task A_member_without_bars_is_counted_but_not_priced()
    {
        Add("AAA", Day0, CoverageReason.Held);
        Series("SPY", Day0, 120, 400m, 440m);

        var overall = Headline(await Block(Day0.AddDays(100))).Overall;

        overall.NotPriced.Should().Be(1);
        overall.Graded.Should().Be(0);
        overall.Status.Should().Be("NotEnoughHistory");
    }
}

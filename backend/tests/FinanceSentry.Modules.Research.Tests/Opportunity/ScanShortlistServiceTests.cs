namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Stage 1 of the #558 funnel driven end to end over its three signal sources: the constituent list,
/// the quote read and the street feed. What matters here is the upstream cost it commits to
/// (EDGAR is asked about the slate, never about the index) and that every source is allowed to fail
/// without taking the nightly cycle with it.
/// </summary>
public sealed class ScanShortlistServiceTests
{
    private static readonly string[] Index =
        [.. Enumerable.Range(0, 50).Select(i => FormattableString.Invariant($"IDX{i:D2}"))];

    [Fact]
    public async Task GradesOnlyTheSlate_NotTheWholeIndex()
    {
        var harness = new Harness(new OpportunityOptions { ScanShortlistGradeBudget = 5, ScanShortlistSize = 3 });
        harness.QuoteEveryConstituent();

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().HaveCount(3);
        harness.GradedTickers.Should().HaveCount(5, "the grading budget is what bounds the EDGAR fan-out");
        harness.GradedTickers.Should().BeSubsetOf(Index);
    }

    [Fact]
    public async Task RanksTheDaysStrongestMovers_WhenTheStreetIsSilent()
    {
        var harness = new Harness(new OpportunityOptions
        {
            ScanShortlistGradeBudget = 3,
            ScanShortlistSize = 3,
            ScanShortlistStreetWeight = 0m,
        });
        harness.QuoteEveryConstituent();

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().BeEquivalentTo(new[] { "IDX49", "IDX48", "IDX47" });
    }

    [Fact]
    public async Task CountsUpgradesNewCoverageAndTargetRaises_ButNotACut()
    {
        var harness = new Harness(new OpportunityOptions
        {
            ScanShortlistGradeBudget = 3,
            ScanShortlistSize = 3,
            ScanShortlistStreetWeight = 1m,
        });
        harness.Quote("IDX01", change: 0m);
        harness.Quote("IDX02", change: 0m);
        harness.Quote("IDX03", change: 0m);
        harness.Quote("IDX04", change: 0m);
        harness.Action("IDX01", AnalystActionType.Upgrade);
        harness.Action("IDX02", AnalystActionType.Initiate);
        harness.Action("IDX03", AnalystActionType.TargetChange, priorTarget: 100m, newTarget: 130m);
        harness.Action("IDX04", AnalystActionType.TargetChange, priorTarget: 130m, newTarget: 100m);

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().BeEquivalentTo(new[] { "IDX01", "IDX02", "IDX03" });
        shortlist.Should().NotContain("IDX04", "a target cut is the same action type, and it is not a reason to look");
    }

    [Fact]
    public async Task AQuoteOutage_LeavesStageOneRankingOnStreetActionsAlone()
    {
        var harness = new Harness(new OpportunityOptions { ScanShortlistSize = 5 });
        harness.FailQuotes();
        harness.Action("IDX07", AnalystActionType.Upgrade);

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().Equal(["IDX07"]);
    }

    [Fact]
    public async Task AStreetOutage_LeavesStageOneRankingOnQuotesAlone()
    {
        var harness = new Harness(new OpportunityOptions { ScanShortlistGradeBudget = 2, ScanShortlistSize = 2 });
        harness.QuoteEveryConstituent();
        harness.FailStreetFeed();

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().HaveCount(2);
    }

    [Fact]
    public async Task EveryUpstreamDown_YieldsAnEmptyShortlistRatherThanAnException()
    {
        var harness = new Harness(new OpportunityOptions());
        harness.FailQuotes();
        harness.FailStreetFeed();

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().BeEmpty("an empty shortlist leaves the universe at its core members");
        harness.GradedTickers.Should().BeEmpty("nothing to grade means no EDGAR cost at all");
    }

    [Fact]
    public async Task AnEmptyConstituentList_CostsNoUpstreamCallAtAll()
    {
        var harness = new Harness(new OpportunityOptions(), index: []);

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().BeEmpty();
        harness.QuoteReads.Should().Be(0);
    }

    [Fact]
    public async Task TheFundamentalsGradeReordersTheSlate_NotJustTheDaysMove()
    {
        var harness = new Harness(new OpportunityOptions { ScanShortlistGradeBudget = 3, ScanShortlistSize = 3 });
        harness.QuoteEveryConstituent();

        // IDX47 is the weakest of the three strongest movers, and the only one compounding revenue.
        harness.GrowsRevenueBy("IDX47", 0.60m);
        harness.GrowsRevenueBy("IDX48", -0.20m);
        harness.GrowsRevenueBy("IDX49", -0.20m);

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().Equal(
            ["IDX47", "IDX49", "IDX48"], "quality is half the shortlist score, so it can overturn the day's move");
    }

    [Fact]
    public async Task ATickerEdgarCannotGrade_KeepsItsPlaceBelowTheGradedNames()
    {
        var harness = new Harness(new OpportunityOptions { ScanShortlistGradeBudget = 3, ScanShortlistSize = 3 });
        harness.QuoteEveryConstituent();
        harness.FailGradeFor("IDX49");

        var shortlist = await harness.Service.GetShortlistAsync();

        shortlist.Should().Equal(
            ["IDX48", "IDX47", "IDX49"],
            "the strongest mover is ungraded, so it keeps its slot but sorts below every graded name");
    }

    private sealed class Harness
    {
        private readonly Dictionary<string, QuoteCacheEntry> quotes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<AnalystAction> actions = [];
        private readonly HashSet<string> ungradable = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, decimal> revenueGrowth = new(StringComparer.OrdinalIgnoreCase);
        private readonly string[] index;
        private bool quotesFail;
        private bool streetFails;

        public Harness(OpportunityOptions options, string[]? index = null)
        {
            this.index = index ?? Index;

            var constituents = new Mock<IIndexConstituentSource>();
            constituents.Setup(c => c.GetConstituents()).Returns(() => this.index);

            var marketData = new Mock<IMarketDataService>();
            marketData.Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    this.QuoteReads++;
                    return this.quotesFail
                        ? throw new HttpRequestException("quote endpoint down")
                        : (IReadOnlyDictionary<string, QuoteCacheEntry>)this.quotes;
                });

            var street = new Mock<IAnalystActionRepository>();
            street.Setup(s => s.QueryAsync(
                    null, It.IsAny<DateOnly>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => this.streetFails
                    ? throw new InvalidOperationException("feed read failed")
                    : (IReadOnlyList<AnalystAction>)this.actions);

            var edgar = new Mock<ISecEdgarService>();
            edgar.Setup(e => e.GetFundamentalsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string ticker, int _, CancellationToken _) =>
                {
                    this.GradedTickers.Add(ticker);
                    return this.ungradable.Contains(ticker)
                        ? throw new HttpRequestException($"EDGAR has no answer for {ticker}")
                        : Filings(ticker, this.revenueGrowth.TryGetValue(ticker, out var growth) ? growth : 0.05m);
                });

            this.Service = new ScanShortlistService(
                constituents.Object,
                marketData.Object,
                street.Object,
                edgar.Object,
                Options.Create(options),
                NullLogger<ScanShortlistService>.Instance);
        }

        public ScanShortlistService Service { get; }

        public List<string> GradedTickers { get; } = [];

        public int QuoteReads { get; private set; }

        /// <summary>IDX00..IDX49 quoted at a rising day's change, so momentum ordering is unambiguous.</summary>
        public void QuoteEveryConstituent()
        {
            for (var i = 0; i < this.index.Length; i++)
            {
                this.Quote(this.index[i], i * 0.1m);
            }
        }

        public void Quote(string ticker, decimal change)
            => this.quotes[ticker] = new QuoteCacheEntry
            {
                Ticker = ticker,
                PreviousClose = 100m,
                Price = 100m + change,
            };

        public void Action(
            string ticker,
            AnalystActionType type,
            decimal? priorTarget = null,
            decimal? newTarget = null)
            => this.actions.Add(new AnalystAction
            {
                Ticker = ticker,
                Firm = "Test Capital",
                ActionType = type,
                PriorTarget = priorTarget,
                NewTarget = newTarget,
                ActionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Source = "test",
            });

        public void FailQuotes() => this.quotesFail = true;

        public void FailStreetFeed() => this.streetFails = true;

        public void FailGradeFor(string ticker) => this.ungradable.Add(ticker);

        /// <summary>Year-on-year revenue growth EDGAR reports for a ticker — the fundamentals grade's input.</summary>
        public void GrowsRevenueBy(string ticker, decimal yoy) => this.revenueGrowth[ticker] = yoy;

        /// <summary>Two comparable quarters, so <c>FundamentalsScorer</c> has a year-on-year to score.</summary>
        private static IReadOnlyList<FundamentalFact> Filings(string ticker, decimal revenueYoy)
            =>
            [
                new(ticker, "Revenue", "Revenue", "USD", 100m * (1m + revenueYoy), new DateOnly(2026, 5, 31), "Q2",
                    2026, "10-Q"),
                new(ticker, "Revenue", "Revenue", "USD", 100m, new DateOnly(2025, 5, 31), "Q2", 2025, "10-Q"),
            ];
    }
}

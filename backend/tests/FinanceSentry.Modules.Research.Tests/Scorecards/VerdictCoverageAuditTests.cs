namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Issue #1413: the verdict-coverage audit names, per held, thesis, watched and coverage symbol, the state of each
/// section a verdict reads and a typed gap reason. One test per reason of <see cref="VerdictCoverageGap"/>, plus the
/// universe rules (origins, order, cap), isolation of a failing read, and that the audit stores nothing.
/// </summary>
public sealed class VerdictCoverageAuditTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task AHeldEquityWithEverySourceAnswering_HasNoGapInAnySection()
    {
        var harness = new Harness();
        harness.Hold("AAA", "STK");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Symbol.Should().Be("AAA");
        row.Origins.Should().Equal(VerdictCoverageOrigins.Held);
        row.Book.Should().Be(new CoverageBookFacts("IBKR", "STK", "Equity", null));
        row.Universe.Gap.Should().BeNull();
        row.Structure.Gap.Should().BeNull();
        row.Fundamentals.Gap.Should().BeNull();
        row.Valuation.Gap.Should().BeNull();
        row.Peers.Gap.Should().BeNull();
        row.Verdict.Gap.Should().BeNull();
        row.Verdict.Word.Should().NotBeNullOrEmpty();
        row.Verdict.FiredRuleId.Should().NotBeNullOrEmpty();
        row.LookThrough.Should().Be(new CoverageSection(VerdictCoverageGap.NotApplicable, "single name"));
    }

    [Fact]
    public async Task TheUniverse_IsHeldThenThesisThenWatchedThenCoverage_OneRowPerSymbolWithEveryOrigin()
    {
        var harness = new Harness();
        harness.Hold("HHH", "STK");
        harness.Thesis.Add("tt");
        harness.Thesis.Add("hhh");
        harness.Watched.AddRange(["WW", " ww ", ""]);
        harness.Coverage.AddRange(["CC", "TT"]);

        var audit = await harness.AuditAsync();

        audit.Rows.Select(r => r.Symbol).Should().Equal("HHH", "TT", "WW", "CC");
        audit.Rows.Select(r => r.Origins).Should().SatisfyRespectively(
            o => o.Should().Equal(VerdictCoverageOrigins.Held, VerdictCoverageOrigins.Thesis),
            o => o.Should().Equal(VerdictCoverageOrigins.Thesis, VerdictCoverageOrigins.Coverage),
            o => o.Should().Equal(VerdictCoverageOrigins.Watched),
            o => o.Should().Equal(VerdictCoverageOrigins.Coverage));
        audit.SymbolCount.Should().Be(4);
        audit.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task MoreSymbolsThanTheCap_AreCut_AndSaidToBe()
    {
        var harness = new Harness();
        harness.Watched.AddRange(Enumerable.Range(1, GetVerdictCoverageAuditQuery.MaxSymbols + 1).Select(i => $"W{i:000}"));

        var audit = await harness.AuditAsync();

        audit.Rows.Should().HaveCount(GetVerdictCoverageAuditQuery.MaxSymbols);
        audit.SymbolCount.Should().Be(GetVerdictCoverageAuditQuery.MaxSymbols + 1);
        audit.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task SourceMissing_AWatchedNameTheHoldingsFeedDoesNotKnow()
    {
        var harness = new Harness();
        harness.Watched.Add("WWW");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Book.Gap.Should().Be(VerdictCoverageGap.SourceMissing);
        row.Book.Provider.Should().BeNull();
        row.Universe.Gap.Should().BeNull("a watched name is always in the universe");
    }

    [Fact]
    public async Task OutsideUniverse_AHeldNameThatIsNotAnEquityAndOnNoList()
    {
        var harness = new Harness();
        harness.Hold("OPTX", "OPT");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Universe.Should().Be(new CoverageSection(VerdictCoverageGap.OutsideUniverse, "OPT"));
    }

    [Fact]
    public async Task OutsideUniverse_DoesNotApply_OnceTheNameIsAlsoWatched()
    {
        var harness = new Harness();
        harness.Hold("OPTX", "OPT");
        harness.Watched.Add("OPTX");

        (await harness.AuditAsync()).Rows.Single().Universe.Gap.Should().BeNull();
    }

    [Fact]
    public async Task SymbolMapping_NoBarsForTheBareSymbol_ReachesStructureAndAnUnavailableValuation()
    {
        var harness = new Harness();
        harness.Hold("ABCD.L", "STK");
        harness.Structures["ABCD.L"] = null;
        harness.NoValuation("ABCD.L");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Structure.Gap.Should().Be(VerdictCoverageGap.SymbolMapping);
        row.Valuation.Gap.Should().Be(VerdictCoverageGap.SymbolMapping);
    }

    [Fact]
    public async Task SymbolMapping_ANameOutsideTheSecTickerMap_HasNoFundamentals()
    {
        var harness = new Harness();
        harness.Hold("FOREIGN", "STK");
        harness.Fundamentals["FOREIGN"] = new FundamentalsResult(
            "FOREIGN", [], new FundamentalsCoverage(
                FundamentalsCoverageStatus.NoSourceAvailable, FundamentalsIssuerType.NotSecRegistrant, "not in the SEC map", []));

        (await harness.AuditAsync()).Rows.Single().Fundamentals.Gap.Should().Be(VerdictCoverageGap.SymbolMapping);
    }

    [Fact]
    public async Task FeedLimit_StaleBarsAndAnUnavailableSource_NameTheFeedNotTheSymbol()
    {
        var harness = new Harness();
        harness.Hold("STALE", "STK");
        harness.Structures["STALE"] = Snapshot("STALE", stale: true);
        harness.Fundamentals["STALE"] = new FundamentalsResult(
            "STALE", [], new FundamentalsCoverage(
                FundamentalsCoverageStatus.SourceUnavailable, null, null, [], "http_500"));
        harness.NoValuation("STALE");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Structure.Should().Be(new CoverageSection(VerdictCoverageGap.FeedLimit, "stale bars"));
        row.Fundamentals.Should().Be(new CoverageSection(VerdictCoverageGap.FeedLimit, "http_500"));
        row.Valuation.Gap.Should().Be(VerdictCoverageGap.FeedLimit);
    }

    [Fact]
    public async Task FeedLimit_FewerPeersThanThePriceLensNeeds()
    {
        var harness = new Harness();
        harness.Hold("THIN", "STK");
        harness.Peers["THIN"] = ["P1"];

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Peers.Should().Be(new CoverageSection(VerdictCoverageGap.FeedLimit, "1 of 3 peers with a forward P/E"));
        row.Valuation.Gap.Should().Be(VerdictCoverageGap.FeedLimit);
    }

    [Fact]
    public async Task NotApplicable_ACryptoPosition_ReadsNoFundamentalsValuationPeersOrLookThrough()
    {
        var harness = new Harness();
        harness.Hold("BTC", "CRYPTO", assetClass: "Crypto", provider: "Binance");
        harness.Watched.Add("BTC");

        var row = (await harness.AuditAsync()).Rows.Single();

        var crypto = new CoverageSection(VerdictCoverageGap.NotApplicable, "crypto");
        row.Fundamentals.Should().Be(crypto);
        row.Valuation.Should().Be(crypto);
        row.Peers.Should().Be(crypto);
        row.LookThrough.Should().Be(crypto);
        harness.FundHoldingsAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task NotApplicable_AnIssuerThatFilesNoCompanyFundamentals()
    {
        var harness = new Harness();
        harness.Hold("FNDX", "STK");
        harness.Fundamentals["FNDX"] = new FundamentalsResult(
            "FNDX", [], new FundamentalsCoverage(
                FundamentalsCoverageStatus.UnsupportedIssuerType, FundamentalsIssuerType.InvestmentFund, null, []));

        (await harness.AuditAsync()).Rows.Single().Fundamentals.Gap.Should().Be(VerdictCoverageGap.NotApplicable);
    }

    [Fact]
    public async Task NoHoldings_AFundWhoseTopHoldingsNoSourceLists()
    {
        var harness = new Harness();
        harness.Hold("ETFX", "STK");
        harness.Fund("ETFX");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Valuation.Gap.Should().Be(VerdictCoverageGap.NotApplicable);
        row.Fundamentals.Gap.Should().Be(VerdictCoverageGap.NotApplicable);
        row.LookThrough.Gap.Should().Be(VerdictCoverageGap.NoHoldings);
    }

    [Fact]
    public async Task Partial_AFundWithFewerThanTenListedHoldings()
    {
        var harness = new Harness();
        harness.Hold("ETFX", "STK");
        harness.Fund("ETFX", new FundHolding("AAA", "AAA", 60m), new FundHolding("BBB", "BBB", 40m));

        var row = (await harness.AuditAsync()).Rows.Single();

        row.LookThrough.Should().Be(new CoverageSection(VerdictCoverageGap.Partial, "Only 2 holdings were listed."));
    }

    [Fact]
    public async Task AFundWithTenHoldings_HasACompleteLookThrough()
    {
        var harness = new Harness();
        harness.Hold("ETFX", "STK");
        harness.Fund("ETFX", Enumerable.Range(1, 10).Select(i => new FundHolding($"H{i}", $"Holding {i}", 10m)).ToArray());

        (await harness.AuditAsync()).Rows.Single().LookThrough.Gap.Should().BeNull();
    }

    [Fact]
    public async Task ScorecardThrew_WhenTheVerdictItselfCannotBeComputed_NamesOnlyTheExceptionType()
    {
        var harness = new Harness { RegimeThrows = true };
        harness.Hold("AAA", "STK");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Verdict.Should().Be(new CoverageVerdict(null, null, null, VerdictCoverageGap.ScorecardThrew, nameof(InvalidOperationException)));
        row.Structure.Gap.Should().BeNull("the other sections were still read");
        row.LookThrough.Detail.Should().Contain(nameof(VerdictCoverageGap.ScorecardThrew));
    }

    [Fact]
    public async Task SectionThrew_AFailingStructureRead_IsolatesToItsSectionAndTheRestOfTheUniverseIsRead()
    {
        var harness = new Harness();
        harness.Hold("BAD", "STK");
        harness.Hold("GOOD", "STK");
        harness.StructureThrows.Add("BAD");

        var rows = (await harness.AuditAsync()).Rows;

        rows[0].Structure.Should().Be(new CoverageSection(VerdictCoverageGap.SectionThrew, nameof(TimeoutException)));
        rows[1].Structure.Gap.Should().BeNull();
        rows[1].Verdict.Gap.Should().BeNull();
    }

    [Fact]
    public async Task SectionThrew_AFailingValuationRead_MarksValuationAndPeers()
    {
        var harness = new Harness();
        harness.Hold("AAA", "STK");
        harness.ValuationThrows.Add("AAA");

        var row = (await harness.AuditAsync()).Rows.Single();

        row.Valuation.Gap.Should().Be(VerdictCoverageGap.SectionThrew);
        row.Peers.Gap.Should().Be(VerdictCoverageGap.SectionThrew);
        row.Structure.Gap.Should().BeNull();
    }

    [Fact]
    public async Task SectionThrew_AFailingFundamentalsRead()
    {
        var harness = new Harness();
        harness.Hold("AAA", "STK");
        harness.FundamentalsThrow.Add("AAA");

        (await harness.AuditAsync()).Rows.Single().Fundamentals.Gap.Should().Be(VerdictCoverageGap.SectionThrew);
    }

    [Fact]
    public async Task ACancelledRead_PropagatesInsteadOfBecomingAGap()
    {
        var harness = new Harness();
        harness.Hold("AAA", "STK");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        harness.StructureCancels = true;

        var act = () => harness.AuditAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AFailingUniverseRead_FailsTheAudit_RatherThanDroppingSymbolsSilently()
    {
        var harness = new Harness();
        harness.Hold("AAA", "STK");
        harness.WatchlistThrows = true;

        var act = () => harness.AuditAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void TheGapReasons_AreTheClosedSetOfThePlan_SerializedByName()
    {
        Enum.GetNames<VerdictCoverageGap>().Should().BeEquivalentTo(
            "SourceMissing", "OutsideUniverse", "SymbolMapping", "FeedLimit", "NotApplicable", "NoHoldings", "Partial", "ScorecardThrew", "SectionThrew");
        System.Text.Json.JsonSerializer.Serialize(new CoverageSection(VerdictCoverageGap.FeedLimit))
            .Should().Contain("\"FeedLimit\"");
    }

    private static MarketStructureSnapshot Snapshot(string ticker, bool stale = false) =>
        new(ticker, new Dictionary<int, decimal?> { [63] = 0.25m }, new Dictionary<int, decimal?> { [63] = 0.25m },
            0.05m, 0m, 1m, 50m, 48m, stale);

    private sealed class Harness
    {
        private readonly Dictionary<string, BrokerageHoldingSummary> brokerage = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<BookFigurePosition> positions = [];
        private readonly Dictionary<string, ValuationCurrentMetrics?> metrics = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> fundSymbols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FundHoldingsLookup> funds = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> fundAsked = [];

        public List<string> Thesis { get; } = [];

        public List<string> Watched { get; } = [];

        public List<string> Coverage { get; } = [];

        public Dictionary<string, MarketStructureSnapshot?> Structures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, FundamentalsResult> Fundamentals { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> Peers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> StructureThrows { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ValuationThrows { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FundamentalsThrow { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool RegimeThrows { get; init; }

        public bool WatchlistThrows { get; set; }

        public bool StructureCancels { get; set; }

        public IReadOnlyList<string> FundHoldingsAsked => fundAsked;

        public void Hold(string symbol, string instrumentType, string assetClass = "Equity", string provider = "IBKR")
        {
            brokerage[symbol] = new BrokerageHoldingSummary(symbol, instrumentType, 1m, 100m, DateTime.UtcNow, provider);
            positions.Add(new BookFigurePosition(symbol, assetClass, 1m, null, 100m, provider));
        }

        public void NoValuation(string symbol) => metrics[symbol] = null;

        public void Fund(string symbol, params FundHolding[] holdings)
        {
            fundSymbols.Add(symbol);
            funds[symbol] = new FundHoldingsLookup(
                FundKind.Fund, holdings, holdings.Length == 0 ? null : FundHoldingsSources.Yahoo, holdings.Length == 0 ? null : DateTimeOffset.UtcNow);
            foreach (var holding in holdings.Where(h => h.Symbol is not null))
            {
                Structures[holding.Symbol!] = Snapshot(holding.Symbol!);
            }
        }

        public Task<VerdictCoverageAudit> AuditAsync(CancellationToken ct = default)
        {
            var book = new Mock<IBookFiguresService>();
            book.Setup(b => b.ReadAsync(User, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BookFigures(0m, 0m, 0m, 0m, 0m, positions, false, []));
            var holdings = new Mock<IBrokerageHoldingsReader>();
            holdings.Setup(h => h.GetHoldingsAsync(User, It.IsAny<CancellationToken>()))
                .ReturnsAsync(brokerage.Values.ToList());
            var thesis = new Mock<IThesisTickerReader>();
            thesis.Setup(t => t.ListTickersAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(Thesis);
            var watch = new Mock<IWatchlistReader>();
            if (WatchlistThrows)
            {
                watch.Setup(w => w.ListTickersAsync(User, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
            }
            else
            {
                watch.Setup(w => w.ListTickersAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(Watched);
            }

            var coverage = new Mock<ICoverageUniverseReader>();
            coverage.Setup(c => c.ListTickersAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(Coverage);

            var valuation = new Mock<IValuationDataService>();
            valuation.Setup(v => v.GetCurrentMetricsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, CancellationToken _) => MetricsFor(symbol));
            valuation.Setup(v => v.GetPeerSymbolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, CancellationToken _) =>
                    Peers.TryGetValue(symbol, out var list) ? list : ["P1", "P2", "P3"]);
            var history = new Mock<IValuationHistoryService>();
            history.Setup(h => h.GetTrailingPeHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TrailingPeHistory(null, null));

            var source = new Mock<IFundHoldingsSource>();
            source.Setup(s => s.GetTopHoldingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, CancellationToken _) =>
                {
                    fundAsked.Add(symbol);
                    return funds.TryGetValue(symbol, out var lookup) ? lookup : FundHoldingsLookup.NotAFund;
                });

            var structure = new StructureReader(this);
            var fundamentals = new FundamentalsReader(this);
            var valuationReader = new ScorecardValuationReader(valuation.Object, history.Object, new PeerForwardPeReader(valuation.Object));
            var verdictOptions = Options.Create(new VerdictOptions());
            var scorecards = new ScorecardReader(
                structure,
                fundamentals,
                new RegimeSource(this),
                Options.Create(new OpportunityOptions()),
                verdictOptions,
                new EtfLookThroughReader(source.Object, valuationReader, NullLogger<EtfLookThroughReader>.Instance));

            var handler = new GetVerdictCoverageAuditQueryHandler(
                book.Object, holdings.Object, watch.Object, thesis.Object, coverage.Object, structure, fundamentals,
                valuationReader, scorecards, verdictOptions,
                NullLogger<GetVerdictCoverageAuditQueryHandler>.Instance);
            return handler.Handle(new GetVerdictCoverageAuditQuery(User), ct);
        }

        private ValuationCurrentMetrics? MetricsFor(string symbol)
        {
            if (ValuationThrows.Contains(symbol))
            {
                throw new HttpRequestException("boom");
            }

            if (metrics.TryGetValue(symbol, out var set))
            {
                return set;
            }

            var notApplicable = fundSymbols.Contains(symbol);
            return new ValuationCurrentMetrics(
                symbol, 100m, 22m, symbol.StartsWith('P') ? 15m : 20m, null, null, null, false, notApplicable, "Technology", "Software");
        }

        private sealed class StructureReader(Harness owner) : IMarketStructureReader
        {
            public Task<MarketStructureSnapshot?> GetStructureAsync(string ticker, CancellationToken ct = default)
            {
                if (owner.StructureCancels)
                {
                    ct.ThrowIfCancellationRequested();
                }

                if (owner.StructureThrows.Contains(ticker))
                {
                    throw new TimeoutException("slow");
                }

                return Task.FromResult(owner.Structures.TryGetValue(ticker, out var snapshot) ? snapshot : Snapshot(ticker));
            }

            public Task<IReadOnlyList<PairwiseCorrelation>> GetPairwiseCorrelationsAsync(
                IReadOnlyCollection<string> tickers, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<PairwiseCorrelation>>([]);

            public Task<IReadOnlyList<UniverseStructureEntry>> GetUniverseStructuresAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<UniverseStructureEntry>>([]);
        }

        private sealed class FundamentalsReader(Harness owner) : IFundamentalsService
        {
            public Task<FundamentalsResult> GetFundamentalsAsync(string ticker, int maxPerConcept, CancellationToken ct = default)
            {
                if (owner.FundamentalsThrow.Contains(ticker))
                {
                    throw new InvalidOperationException("edgar down");
                }

                return Task.FromResult(owner.Fundamentals.TryGetValue(ticker, out var result)
                    ? result
                    : new FundamentalsResult(
                        ticker, [], new FundamentalsCoverage(
                            FundamentalsCoverageStatus.Covered, FundamentalsIssuerType.DomesticFiler, null,
                            [new FundamentalsBasisCoverage("annual", "sec", new DateOnly(2026, 6, 30), false)])));
            }
        }

        private sealed class RegimeSource(Harness owner) : IMarketRegimeSource
        {
            public Task<MarketRegimeSnapshot?> GetLatestAsync(CancellationToken ct = default) =>
                owner.RegimeThrows ? throw new InvalidOperationException("regime down") : Task.FromResult<MarketRegimeSnapshot?>(null);
        }
    }
}

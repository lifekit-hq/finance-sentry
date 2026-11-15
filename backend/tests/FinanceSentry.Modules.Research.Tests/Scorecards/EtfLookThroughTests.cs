namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Issue #1324: an ETF's verdict by look-through of its top 10 holdings, through the real <see cref="ScorecardReader"/>:
/// each holding scored by the single-name rules, the fund's word from the ETF table, the basis published on the
/// scorecard, and the visible degradation when holdings are missing.
/// </summary>
public sealed class EtfLookThroughTests
{
    private const string Fund = "ETFX";

    private static readonly DateTimeOffset AsOf = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AFundWithMostOfItsTopWeightReadingBuy_ReadsBuy_AndPublishesItsBasis()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, Ten(strong: 6));

        var card = await harness.ReadAsync(Fund);

        card.Verdict.Word.Should().Be(nameof(VerdictWord.Buy));
        card.Verdict.FiredRuleId.Should().Be(EtfVerdictRules.Buy);
        card.Verdict.RulesVersion.Should().Be(new VerdictOptions().EtfRulesVersion);
        card.Valuation.NotApplicable.Should().BeTrue();
        var basis = card.LookThrough!;
        basis.Status.Should().Be(ScorecardLookThrough.Complete);
        basis.Note.Should().BeNull();
        basis.Source.Should().Be(FundHoldingsSources.Yahoo);
        basis.AsOf.Should().Be(AsOf);
        basis.CoveredWeightPct.Should().Be(100m);
        basis.BuyWeightPct.Should().Be(60m);
        basis.BuyWeightSharePct.Should().Be(60m);
        basis.BuyWeightSharePctRequired.Should().Be(50m);
        basis.Holdings.Should().HaveCount(10);
        basis.Holdings.Take(6).Should().OnlyContain(h => h.Word == nameof(VerdictWord.Buy));
        basis.Holdings.Skip(6).Should().OnlyContain(h => h.Word == nameof(VerdictWord.Avoid));
        card.Verdict.Reasons.Select(r => r.Axis).Should().Equal("Structure", "Crowding", "LookThrough");
    }

    [Fact]
    public async Task AFundWhoseTopWeightIsNotMostlyBuy_ReadsWait_AndSaysWhatWouldTurnItIntoBuy()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, Ten(strong: 5));

        var card = await harness.ReadAsync(Fund);

        card.Verdict.Word.Should().Be(nameof(VerdictWord.Wait));
        card.Verdict.FiredRuleId.Should().Be(EtfVerdictRules.Otherwise);
        card.LookThrough!.BuyWeightSharePct.Should().Be(50m);
        card.Verdict.TurnsBuyWhen.Should().Be("more than 50% of the top-10 weight reads BUY or better (reads 50%)");
    }

    [Fact]
    public async Task AFundWithABrokenChart_ReadsAvoid_EvenWhenItsHoldingsReadBuy()
    {
        var harness = new Harness(Weak(Fund));
        harness.Holdings(Fund, Ten(strong: 10));

        var card = await harness.ReadAsync(Fund);

        card.Verdict.Word.Should().Be(nameof(VerdictWord.Avoid));
        card.Verdict.FiredRuleId.Should().Be(EtfVerdictRules.WeakStructure);
    }

    [Fact]
    public async Task WithoutHoldings_TheLookThroughIsVisiblyUnavailable_AndTheWordCannotReachBuy()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund);

        var card = await harness.ReadAsync(Fund);

        card.Verdict.Word.Should().Be(nameof(VerdictWord.Wait));
        card.Verdict.FiredRuleId.Should().Be(EtfVerdictRules.Otherwise);
        var basis = card.LookThrough!;
        basis.Status.Should().Be(ScorecardLookThrough.Unavailable);
        basis.Holdings.Should().BeEmpty();
        basis.BuyWeightSharePct.Should().BeNull();
        basis.Note.Should().Be(EtfLookThroughReader.UnavailableNote);
        card.Verdict.Reasons.Single(r => r.Axis == "LookThrough").Status.Should().Be(nameof(VerdictReasonStatus.Missing));
        card.Verdict.Rules.Single(r => r.Id == EtfVerdictRules.Buy).Conditions
            .Single(c => c.Axis == nameof(VerdictAxis.LookThrough)).Actual.Should().BeNull();
    }

    [Fact]
    public async Task WithoutHoldings_AWeakFundIsStillAvoid()
    {
        var harness = new Harness(Weak(Fund));
        harness.Holdings(Fund);

        var card = await harness.ReadAsync(Fund);

        card.Verdict.Word.Should().Be(nameof(VerdictWord.Avoid));
        card.LookThrough!.Status.Should().Be(ScorecardLookThrough.Unavailable);
    }

    [Fact]
    public async Task AHoldingThatCannotBeScored_CountsAgainstTheFund_AndTheBasisSaysSo()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, Ten(strong: 10));
        harness.Throws("S4");
        harness.NoStructure("S5");

        var card = await harness.ReadAsync(Fund);

        var basis = card.LookThrough!;
        basis.Status.Should().Be(ScorecardLookThrough.Partial);
        basis.Note.Should().Be("2 of 10 listed holdings could not be scored and count against the fund.");
        basis.Holdings.Single(h => h.Symbol == "S4").Word.Should().BeNull();
        basis.Holdings.Single(h => h.Symbol == "S5").Word.Should().Be(nameof(VerdictWord.NotScored));
        basis.BuyWeightSharePct.Should().Be(80m);
        card.Verdict.Word.Should().Be(nameof(VerdictWord.Buy));
    }

    [Fact]
    public async Task AFundWithFewerThanTenListedHoldings_IsPartial()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, [new FundHolding("S1", "S1", 60m), new FundHolding("S2", "S2", 40m)]);

        var card = await harness.ReadAsync(Fund);

        card.LookThrough!.Status.Should().Be(ScorecardLookThrough.Partial);
        card.LookThrough.Note.Should().Be("Only 2 holdings were listed.");
        card.LookThrough.BuyWeightSharePct.Should().Be(100m);
    }

    [Fact]
    public async Task ALineWithoutASymbol_CountsAgainstTheFund_WithoutBeingScored()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, [.. Ten(strong: 10).Take(9), new FundHolding(null, "Cash", 10m)]);

        var card = await harness.ReadAsync(Fund);

        card.LookThrough!.Holdings[^1].Word.Should().BeNull();
        card.LookThrough.Status.Should().Be(ScorecardLookThrough.Partial);
        card.LookThrough.BuyWeightSharePct.Should().Be(90m);
    }

    [Fact]
    public async Task AHoldingThatIsItselfAFund_IsNotLookedThrough()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, Ten(strong: 10));
        harness.NotApplicable("S1");

        var card = await harness.ReadAsync(Fund);

        card.LookThrough!.Holdings[0].Word.Should().Be(nameof(VerdictWord.NotScored));
        card.LookThrough.Holdings[0].FiredRuleId.Should().Be(VerdictRules.OutsideUniverse);
        harness.HoldingsAsked.Should().Equal(Fund);
    }

    [Fact]
    public async Task ANonFund_KeepsItsNotScoredVerdict_WithoutALookThrough()
    {
        var harness = new Harness(Strong("BTC"));
        harness.NotAFund("BTC");

        var card = await harness.ReadAsync("BTC");

        card.Verdict.FiredRuleId.Should().Be(VerdictRules.OutsideUniverse);
        card.Verdict.Word.Should().Be(nameof(VerdictWord.NotScored));
        card.LookThrough.Should().BeNull();
    }

    [Fact]
    public async Task WhenTheSourceCannotTell_TheNameKeepsItsNotScoredVerdict()
    {
        var harness = new Harness(Strong(Fund));
        harness.Unknown(Fund);

        var card = await harness.ReadAsync(Fund);

        card.Verdict.FiredRuleId.Should().Be(VerdictRules.OutsideUniverse);
        card.LookThrough.Should().BeNull();
    }

    [Fact]
    public async Task WithLookThroughOff_NoHoldingsAreAsked()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, Ten(strong: 10));

        var card = await harness.ReadAsync(Fund, lookThrough: false);

        card.Verdict.FiredRuleId.Should().Be(VerdictRules.OutsideUniverse);
        card.LookThrough.Should().BeNull();
        harness.HoldingsAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEquity_NeverAsksForHoldings()
    {
        var harness = new Harness(Strong("MSFT"));
        harness.Equity("MSFT");

        var card = await harness.ReadAsync("MSFT");

        card.LookThrough.Should().BeNull();
        card.Verdict.FiredRuleId.Should().NotStartWith("etf_");
        harness.HoldingsAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task TheQueryHandler_ReadsAnEtfThroughItsHoldings()
    {
        var harness = new Harness(Strong(Fund));
        harness.Holdings(Fund, Ten(strong: 7));
        var handler = new GetScorecardQueryHandler(harness.Valuation.Object, harness.History.Object, new PeerForwardPeReader(harness.Valuation.Object), harness.Reader);

        var card = await handler.Handle(new GetScorecardQuery(" etfx "), CancellationToken.None);

        card.Symbol.Should().Be(Fund);
        card.Verdict.Word.Should().Be(nameof(VerdictWord.Buy));
        card.LookThrough!.Holdings.Should().HaveCount(10);
    }

    /// <summary>Ten equal-weight holdings: the first <paramref name="strong"/> chart strong (S1..), the rest broken (W..).</summary>
    private static List<FundHolding> Ten(int strong) =>
        Enumerable.Range(1, 10)
            .Select(i => new FundHolding(i <= strong ? $"S{i}" : $"W{i}", $"Holding {i}", 10m))
            .ToList();

    private static MarketStructureSnapshot Strong(string ticker) => Snapshot(ticker, 0.25m, 0.05m, 0m);

    private static MarketStructureSnapshot Weak(string ticker) => Snapshot(ticker, -0.25m, -0.5m, -5m);

    private static MarketStructureSnapshot Snapshot(string ticker, decimal rs, decimal extension, decimal zScore) =>
        new(ticker, new Dictionary<int, decimal?> { [63] = rs }, new Dictionary<int, decimal?> { [63] = rs },
            extension, zScore, 1m, 50m, 48m, false);

    private sealed class Harness
    {
        private readonly Dictionary<string, MarketStructureSnapshot?> structures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FundHoldingsLookup> funds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> notApplicable = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> throwing = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> asked = [];
        private readonly string fundSymbol;

        public Harness(MarketStructureSnapshot fundStructure)
        {
            fundSymbol = fundStructure.Ticker;
            structures[fundSymbol] = fundStructure;
            notApplicable.Add(fundSymbol);

            Valuation = new Mock<IValuationDataService>();
            Valuation.Setup(v => v.GetCurrentMetricsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, CancellationToken _) =>
                {
                    if (throwing.Contains(symbol))
                    {
                        throw new HttpRequestException("boom");
                    }

                    return new ValuationCurrentMetrics(
                        symbol, 100m, null, null, null, null, null, IsStale: false, NotApplicable: notApplicable.Contains(symbol), null, null);
                });
            History = new Mock<IValuationHistoryService>();
            History.Setup(h => h.GetTrailingPeHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TrailingPeHistory(null, null));

            var source = new Mock<IFundHoldingsSource>();
            source.Setup(s => s.GetTopHoldingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string symbol, CancellationToken _) =>
                {
                    asked.Add(symbol);
                    return funds.TryGetValue(symbol, out var lookup) ? lookup : FundHoldingsLookup.NotAFund;
                });

            var verdictOptions = Options.Create(new VerdictOptions());
            var opportunity = Options.Create(new OpportunityOptions());
            Reader = new ScorecardReader(
                new PerTickerStructureReader(structures),
                new FakeFundamentalsService(),
                new FakeMarketRegimeSource(),
                opportunity,
                verdictOptions,
                new EtfLookThroughReader(
                    source.Object,
                    new ScorecardValuationReader(Valuation.Object, History.Object, new PeerForwardPeReader(Valuation.Object)),
                    NullLogger<EtfLookThroughReader>.Instance));
        }

        public Mock<IValuationDataService> Valuation { get; }

        public Mock<IValuationHistoryService> History { get; }

        public ScorecardReader Reader { get; }

        public IReadOnlyList<string> HoldingsAsked => asked;

        public void Holdings(string fund, IReadOnlyList<FundHolding>? holdings = null)
        {
            var list = holdings ?? [];
            funds[fund] = new FundHoldingsLookup(
                FundKind.Fund, list, list.Count == 0 ? null : FundHoldingsSources.Yahoo, list.Count == 0 ? null : AsOf);

            foreach (var symbol in list.Select(h => h.Symbol).OfType<string>())
            {
                structures[symbol] = symbol.StartsWith('S') ? Strong(symbol) : Weak(symbol);
            }
        }

        public void NotAFund(string symbol) => funds[symbol] = FundHoldingsLookup.NotAFund;

        public void Unknown(string symbol) => funds[symbol] = FundHoldingsLookup.Unknown;

        public void Equity(string symbol) => notApplicable.Remove(symbol);

        public void NotApplicable(string symbol) => notApplicable.Add(symbol);

        public void Throws(string symbol) => throwing.Add(symbol);

        public void NoStructure(string symbol) => structures[symbol] = null;

        public async Task<ScorecardResult> ReadAsync(string symbol, bool lookThrough = true)
        {
            var (axis, sector, industry, peerForwardPes) = await new ScorecardValuationReader(
                    Valuation.Object, History.Object, new PeerForwardPeReader(Valuation.Object))
                .ReadAsync(symbol, CancellationToken.None);
            return await Reader.ReadAsync(
                symbol, axis, null, CancellationToken.None, sector, industry, peerForwardPes, lookThrough);
        }
    }

    private sealed class PerTickerStructureReader(IReadOnlyDictionary<string, MarketStructureSnapshot?> structures)
        : IMarketStructureReader
    {
        public Task<MarketStructureSnapshot?> GetStructureAsync(string ticker, CancellationToken ct = default) =>
            Task.FromResult(structures.TryGetValue(ticker, out var snapshot) ? snapshot : null);

        public Task<IReadOnlyList<PairwiseCorrelation>> GetPairwiseCorrelationsAsync(
            IReadOnlyCollection<string> tickers, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PairwiseCorrelation>>([]);

        public Task<IReadOnlyList<UniverseStructureEntry>> GetUniverseStructuresAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UniverseStructureEntry>>([]);
    }
}

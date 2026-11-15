namespace FinanceSentry.Modules.Research.Tests.Valuation;

using System.Text.Json;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Valuation;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// DCF compose logic (#1269) over synthetic statements: the happy path (grid centres equal the headline, the reverse
/// DCF reproduces the price), every override, every fallback flag, every not-applicable / not-evaluable path with its
/// typed reason and no number, the equity bridge, the exit-multiple cross-check and the short-reply shape.
/// Synthetic target (amounts in millions): revenue 1,000 / 1,100 / 1,200 at a flat 20% operating margin, D&amp;A 5%
/// and capex 6% of revenue, net debt 300 over 100 shares priced at 20, beta 1.0 and a 4% 10-year yield.
/// </summary>
public sealed class GetDcfValuationQueryTests
{
    private const decimal Price = 20m;
    private const decimal TenYearYield = 4m;

    private static DcfAnnualPeriod Year(
        int year, decimal revenue, decimal? tax = null, decimal? interest = null, decimal? workingCapital = null,
        decimal? depreciation = null, decimal? capex = null, decimal margin = 0.20m)
        => new(
            new DateOnly(year, 12, 31), revenue, revenue * margin, tax, tax is null ? null : revenue * 0.175m,
            interest, depreciation ?? revenue * 0.05m, capex ?? -revenue * 0.06m, workingCapital);

    // Tax 35 / 40 / 45 over pretax 175 / 192.5 / 210 -> a 20.8% effective rate; working capital -6 / -5 (2025 / 2024).
    private static List<DcfAnnualPeriod> History() =>
    [
        Year(2025, 1200m, tax: 45m, interest: 30m, workingCapital: -6m),
        Year(2024, 1100m, tax: 40m, interest: 28m, workingCapital: -5m),
        Year(2023, 1000m, tax: 35m, interest: null, workingCapital: null),
    ];

    private static ValuationCurrentMetrics Metrics(
        string ticker = "TGT",
        decimal? price = Price,
        decimal? beta = 1.0m,
        decimal? debt = 400m,
        decimal? cash = 100m,
        decimal? shares = 100m,
        decimal? marketCap = null,
        string? industry = "Restaurants",
        string? currency = "USD",
        string? financialCurrency = "USD",
        decimal? ev = 2300m,
        decimal? ebitda = 300m,
        bool notApplicable = false) => new(
        ticker, price, TrailingPe: null, ForwardPe: null, EvToEbitda: null, DividendYield: null, ConsensusTarget: null,
        IsStale: false, NotApplicable: notApplicable, "Consumer Cyclical", industry, Name: ticker,
        Currency: currency, MarketCap: marketCap,
        Comps: new ValuationCompsInputs(
            ev, ebitda, 1200m, 0.09m, 0.25m, null, 2m, null, debt, cash, shares, financialCurrency),
        Beta: beta);

    private sealed class StubInputs(DcfInputs? inputs) : IDcfInputsSource
    {
        public Task<DcfInputs?> GetAsync(string ticker, CancellationToken ct = default) => Task.FromResult(inputs);
    }

    private sealed class StubFundamentals(FundamentalsCoverage coverage) : IFundamentalsService
    {
        public Task<FundamentalsResult> GetFundamentalsAsync(string ticker, int maxPerConcept, CancellationToken ct = default)
            => Task.FromResult(new FundamentalsResult(ticker, [], coverage));
    }

    private sealed class StubYield(decimal? yieldPercent) : IMarketDataService
    {
        public Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
            IReadOnlyCollection<string> tickers, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, QuoteCacheEntry>>(
                yieldPercent is { } y && tickers.Contains("^TNX")
                    ? new Dictionary<string, QuoteCacheEntry> { ["^TNX"] = new() { Ticker = "^TNX", Price = y } }
                    : new Dictionary<string, QuoteCacheEntry>());

        public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(string ticker, DateOnly since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DailyClose>>([]);
    }

    private static FundamentalsCoverage Covered => new(FundamentalsCoverageStatus.Covered, null, null, []);

    private static Task<DcfValuationResult> Run(
        ValuationCurrentMetrics? target = null,
        IReadOnlyList<DcfAnnualPeriod>? history = null,
        GetDcfValuationQuery? query = null,
        decimal? yield = TenYearYield,
        FundamentalsCoverage? coverage = null,
        bool noHistory = false,
        DcfInputs? inputs = null,
        params ValuationCurrentMetrics[] peers)
    {
        var data = new FakeValuationDataService();
        var tgt = target ?? Metrics();
        data.Metrics[tgt.Ticker] = tgt;
        foreach (var peer in peers)
        {
            data.Metrics[peer.Ticker] = peer;
            data.DefaultPeers.Add(peer.Ticker);
        }

        var dcfInputs = noHistory ? null : inputs ?? new DcfInputs(history ?? History(), null, null);
        IQueryHandler<GetCompsValuationQuery, CompsValuationResult> comps = new GetCompsValuationQueryHandler(data);
        var handler = new GetDcfValuationQueryHandler(
            data,
            new StubFundamentals(coverage ?? Covered),
            new StubInputs(dcfInputs),
            new StubYield(yield),
            comps,
            Options.Create(new ValuationOptions()),
            TimeProvider.System);
        return handler.Handle(query ?? new GetDcfValuationQuery(tgt.Ticker), default);
    }

    private static ValuationCurrentMetrics Peer(string ticker, decimal multiple) => Metrics(ticker, ev: multiple * 300m);

    private static ValuationCurrentMetrics[] FourPeers() =>
        [Peer("P1", 8m), Peer("P2", 10m), Peer("P3", 14m), Peer("P4", 16m)];

    private static void NoNumbers(DcfValuationResult r)
    {
        r.Summary.Should().BeNull();
        r.Assumptions.Should().BeNull();
        r.Sensitivity.Should().BeNull();
        r.ExitMultipleCrossCheck.Should().BeNull();
    }

    // --- happy path ---

    [Fact]
    public async Task Happy_path_returns_value_range_implied_growth_and_assumptions()
    {
        var r = await Run(peers: FourPeers());

        r.NotApplicable.Should().BeFalse();
        r.NotEvaluableReason.Should().BeNull();
        var s = r.Summary!;
        s.Price.Should().Be(Price);
        s.ValuePerShare.Should().BeGreaterThan(0m);
        s.UpsidePct.Should().Be(Math.Round((s.ValuePerShare / Price - 1m) * 100m, 1));
        s.RangeLow.Should().BeLessThan(s.ValuePerShare);
        s.RangeHigh.Should().BeGreaterThan(s.ValuePerShare);
        s.HistoricalCagrPct.Should().BeApproximately(9.5m, 0.2m, "1,000 -> 1,200 over two years is 9.5% a year");
        s.TerminalShareOfEvPct.Should().BeInRange(0m, 100m);
        s.ImpliedGrowthPct.Should().NotBeNull();

        var a = r.Assumptions!;
        a.Years.Should().Be(5);
        a.RiskFree.Should().Be(0.04m);
        a.Beta.Should().Be(1.0m);
        a.EquityRiskPremium.Should().Be(0.05m);
        a.OperatingMargin.Should().Be(0.20m);
        a.DepreciationPct.Should().Be(0.05m);
        a.CapexPct.Should().Be(0.06m);
        a.TaxRate.Should().BeApproximately(0.2078m, 0.0005m, "120 tax over 577.5 pretax income");
        a.NwcPctOfRevenueChange.Should().BeApproximately(0.055m, 0.001m, "11 invested over a 200 revenue increase");
        a.Wacc.Should().BeInRange(0.07m, 0.09m, "cost of equity 9%, with a cheaper after-tax debt weighted in");
        a.TerminalGrowth.Should().Be(0.025m);
        a.GrowthPath.Should().HaveCount(5);
        a.GrowthPath[0].Should().BeApproximately(0.095m, 0.002m);
        a.GrowthPath[^1].Should().Be(0.025m, "growth fades to the terminal rate by the last year");
        a.BaseRevenue.Should().Be(1200m);
        r.Sources.Should().Contain(["yahoo:fundamentals-timeseries", "yahoo:quoteSummary", "yahoo:^TNX"]);
        r.Flags.Should().NotContain(DcfFlags.OverridesApplied);
    }

    [Fact]
    public async Task Both_grids_are_5x5_full_recomputes_and_their_centres_equal_the_headline()
    {
        var r = await Run(peers: FourPeers());

        foreach (var grid in new[] { r.Sensitivity!.WaccByGrowth, r.Sensitivity.GrowthByMargin })
        {
            grid.Rows.Should().HaveCount(5);
            grid.Cols.Should().HaveCount(5);
            grid.Values.Should().HaveCount(5).And.OnlyContain(row => row.Count == 5);
            grid.Values[2][2].Should().Be(r.Summary!.ValuePerShare);
        }

        var wacc = r.Sensitivity!.WaccByGrowth;
        wacc.RowAxis.Should().Be("wacc");
        wacc.ColAxis.Should().Be("terminalGrowth");
        wacc.Rows[2].Should().Be(r.Assumptions!.Wacc);
        (wacc.Rows[4] - wacc.Rows[0]).Should().Be(0.02m, "WACC base +/- 1.0pt in 0.5pt steps");
        wacc.Cols[2].Should().Be(r.Assumptions.TerminalGrowth);
        (wacc.Cols[4] - wacc.Cols[0]).Should().Be(0.02m);

        var margin = r.Sensitivity.GrowthByMargin;
        margin.RowAxis.Should().Be("year1Growth");
        margin.ColAxis.Should().Be("operatingMargin");
        (margin.Rows[4] - margin.Rows[0]).Should().Be(0.04m, "year-1 growth base +/- 2pt in 1pt steps");
        margin.Cols[2].Should().Be(0.20m);
        (margin.Cols[4] - margin.Cols[0]).Should().Be(0.04m);

        // value falls as WACC rises and rises with margin
        wacc.Values[0][2].Should().BeGreaterThan(wacc.Values[4][2]!.Value);
        margin.Values[2][4].Should().BeGreaterThan(margin.Values[2][0]!.Value);

        r.Summary!.RangeLow.Should().Be(wacc.Values.SelectMany(v => v).OfType<decimal>().Min());
        r.Summary.RangeHigh.Should().Be(wacc.Values.SelectMany(v => v).OfType<decimal>().Max());
    }

    [Fact]
    public async Task Reverse_dcf_growth_reproduces_the_current_price_within_half_a_percent()
    {
        var r = await Run(peers: FourPeers());
        var a = r.Assumptions!;

        // Rebuild the model from the echoed assumptions and run it at the implied constant growth.
        var drivers = new DcfMath.Drivers(
            a.BaseRevenue, a.OperatingMargin, a.TaxRate, a.DepreciationPct, a.CapexPct, a.NwcPctOfRevenueChange);
        var scenario = new DcfMath.Scenario(
            drivers, DcfMath.ConstantGrowthPath(r.Summary!.ImpliedGrowthPct!.Value / 100m, a.Years), a.Wacc, a.TerminalGrowth);
        var ev = DcfMath.Value(scenario)!.EnterpriseValue;
        var perShare = CompsMath.PerShareFromEnterpriseValue(ev, netDebt: 300m, shares: 100m);

        perShare.Should().BeApproximately(Price, Price * 0.005m + 0.01m);
    }

    // --- not applicable / not evaluable: a typed reason and no number ---

    [Fact]
    public async Task Non_equity_ticker_is_not_applicable()
    {
        var r = await Run(target: Metrics(notApplicable: true));

        r.NotApplicable.Should().BeTrue();
        r.NotEvaluableReason.Should().BeNull();
        NoNumbers(r);
    }

    [Fact]
    public async Task Unknown_ticker_is_data_unavailable()
    {
        var data = new FakeValuationDataService();
        var handler = new GetDcfValuationQueryHandler(
            data, new StubFundamentals(Covered), new StubInputs(null), new StubYield(4m),
            new GetCompsValuationQueryHandler(data), Options.Create(new ValuationOptions()), TimeProvider.System);

        var r = await handler.Handle(new GetDcfValuationQuery("nope"), default);

        r.Ticker.Should().Be("NOPE");
        r.NotEvaluableReason.Should().Be(DcfReasons.DataUnavailable);
        NoNumbers(r);
    }

    [Theory]
    [InlineData("Banks - Regional")]
    [InlineData("Insurance - Life")]
    public async Task Banks_and_insurers_are_financial_sector(string industry)
    {
        var r = await Run(target: Metrics(industry: industry));

        r.NotEvaluableReason.Should().Be(DcfReasons.FinancialSector);
        NoNumbers(r);
    }

    [Fact]
    public async Task A_coverage_status_other_than_covered_passes_through_unchanged()
    {
        var coverage = new FundamentalsCoverage(
            FundamentalsCoverageStatus.NoSourceAvailable, null, "sec-edgar-us-gaap: nothing; yahoo-finance: nothing", []);

        var r = await Run(coverage: coverage);

        r.NotEvaluableReason.Should().Be(FundamentalsCoverageStatus.NoSourceAvailable);
        r.Detail.Should().Be("sec-edgar-us-gaap: nothing; yahoo-finance: nothing");
        NoNumbers(r);
    }

    [Fact]
    public async Task Negative_latest_operating_income_is_no_operating_profit()
    {
        var history = new List<DcfAnnualPeriod>
        {
            Year(2025, 1200m, margin: -0.05m), Year(2024, 1100m), Year(2023, 1000m),
        };

        var r = await Run(history: history);

        r.NotEvaluableReason.Should().Be(DcfReasons.NoOperatingProfit);
        NoNumbers(r);
    }

    [Fact]
    public async Task Fewer_than_three_usable_years_is_insufficient_history()
    {
        var r = await Run(history: History().Take(2).ToList());

        r.NotEvaluableReason.Should().Be(DcfReasons.InsufficientHistory);
        NoNumbers(r);
    }

    [Fact]
    public async Task Missing_depreciation_or_capex_is_cash_flow_unavailable_not_zero()
    {
        var noCapex = History().Select(p => p with { CapitalExpenditure = null }).ToList();

        var r = await Run(history: noCapex);

        r.NotEvaluableReason.Should().Be(DcfReasons.CashFlowUnavailable);
        NoNumbers(r);
    }

    [Fact]
    public async Task No_statement_history_or_no_price_is_not_evaluable()
    {
        (await Run(noHistory: true)).NotEvaluableReason.Should().Be(DcfReasons.DataUnavailable);
        (await Run(target: Metrics(price: null))).NotEvaluableReason.Should().Be(DcfReasons.PriceUnavailable);
    }

    [Fact]
    public async Task Missing_beta_or_yield_is_not_evaluable_unless_wacc_is_overridden()
    {
        (await Run(target: Metrics(beta: null))).NotEvaluableReason.Should().Be(DcfReasons.BetaUnavailable);
        (await Run(yield: null)).NotEvaluableReason.Should().Be(DcfReasons.RiskFreeUnavailable);

        var overridden = await Run(
            target: Metrics(beta: null), yield: null, query: new GetDcfValuationQuery("TGT", Wacc: 0.09m));

        overridden.NotEvaluableReason.Should().BeNull();
        overridden.Assumptions!.Wacc.Should().Be(0.09m);
        overridden.Assumptions.RiskFree.Should().BeNull("the source had none and the override made it unnecessary");
        overridden.Assumptions.Beta.Should().BeNull();
    }

    [Fact]
    public async Task Terminal_growth_at_or_above_wacc_is_refused()
    {
        var r = await Run(query: new GetDcfValuationQuery("TGT", Wacc: 0.05m, TerminalGrowth: 0.05m));

        r.NotEvaluableReason.Should().Be(DcfReasons.TerminalGrowthNotBelowWacc);
        NoNumbers(r);
    }

    [Fact]
    public async Task Debt_beyond_the_enterprise_value_is_negative_equity_value()
    {
        var r = await Run(target: Metrics(debt: 400_000m, cash: 0m));

        r.NotEvaluableReason.Should().Be(DcfReasons.NegativeEquityValue);
        NoNumbers(r);
    }

    // --- equity bridge ---

    [Fact]
    public async Task Missing_debt_and_cash_cannot_be_bridged_and_missing_shares_is_named()
    {
        var noDebt = await Run(target: Metrics(debt: null));
        noDebt.NotEvaluableReason.Should().Be(DcfReasons.EquityBridgeUnavailable);
        noDebt.Flags.Should().Contain(CompsFlags.NetDebtUnavailable);

        var noShares = await Run(target: Metrics(shares: null));
        noShares.NotEvaluableReason.Should().Be(DcfReasons.SharesUnavailable);
    }

    [Fact]
    public async Task Shares_fall_back_to_market_cap_over_price_with_a_flag()
    {
        var r = await Run(target: Metrics(shares: null, marketCap: 2000m));

        r.NotEvaluableReason.Should().BeNull();
        r.Flags.Should().Contain(DcfFlags.SharesFromMarketCap);
        r.Summary!.ValuePerShare.Should().Be((await Run()).Summary!.ValuePerShare, "2,000 / 20 is the same 100 shares");
    }

    [Fact]
    public async Task A_reporting_currency_other_than_the_quote_currency_is_converted_and_flagged()
    {
        var converted = await Run(target: Metrics(financialCurrency: "EUR"));
        converted.Flags.Should().Contain(DcfFlags.FxConverted);
        converted.Summary!.ValuePerShare.Should().NotBe((await Run()).Summary!.ValuePerShare);

        var unknown = await Run(target: Metrics(financialCurrency: "ZZZ"));
        unknown.NotEvaluableReason.Should().Be(DcfReasons.EquityBridgeUnavailable);
        unknown.Flags.Should().Contain(DcfFlags.FxUnknown);
    }

    // --- fallbacks and flags ---

    [Fact]
    public async Task Missing_tax_cost_of_debt_and_working_capital_fall_back_with_flags()
    {
        var bare = History().Select(p => p with
        {
            TaxProvision = null, PretaxIncome = null, InterestExpense = null, ChangeInWorkingCapital = null,
        }).ToList();

        var r = await Run(history: bare);

        r.Flags.Should().Contain([DcfFlags.TaxRateDefaulted, DcfFlags.CostOfDebtDefaulted, DcfFlags.NwcAssumedZero]);
        r.Assumptions!.TaxRate.Should().Be(0.21m);
        r.Assumptions.NwcPctOfRevenueChange.Should().Be(0m);
    }

    [Fact]
    public async Task A_tax_rate_outside_zero_to_forty_percent_is_replaced_with_the_default()
    {
        var odd = History().Select(p => p with { TaxProvision = p.PretaxIncome * 0.6m }).ToList();

        var r = await Run(history: odd);

        r.Flags.Should().Contain(DcfFlags.TaxRateDefaulted);
        r.Assumptions!.TaxRate.Should().Be(0.21m);
    }

    [Fact]
    public async Task Net_cash_leaves_wacc_equal_to_the_cost_of_equity_and_needs_no_cost_of_debt()
    {
        var r = await Run(target: Metrics(debt: 100m, cash: 500m));

        r.Assumptions!.Wacc.Should().Be(0.09m, "4% + 1.0 x 5%");
        r.Flags.Should().NotContain(DcfFlags.CostOfDebtDefaulted);
    }

    [Fact]
    public async Task A_historical_cagr_above_the_cap_is_clamped_to_year_one_growth_of_twenty_five_percent()
    {
        var fast = new List<DcfAnnualPeriod> { Year(2025, 2000m), Year(2024, 1300m), Year(2023, 1000m) };

        var r = await Run(history: fast);

        r.Flags.Should().Contain(DcfFlags.GrowthCapped);
        r.Assumptions!.GrowthPath[0].Should().Be(0.25m);
        r.Summary!.HistoricalCagrPct.Should().BeGreaterThan(25m, "the reported CAGR stays the real one");
    }

    [Fact]
    public async Task Terminal_growth_is_capped_at_the_risk_free_rate()
    {
        var r = await Run(yield: 2m);

        r.Assumptions!.TerminalGrowth.Should().Be(0.02m);
        r.Flags.Should().Contain(DcfFlags.TerminalGrowthCapped);
    }

    [Fact]
    public async Task A_terminal_value_carrying_most_of_the_answer_is_flagged()
    {
        var heavy = await Run(query: new GetDcfValuationQuery("TGT", Wacc: 0.06m, TerminalGrowth: 0.05m));

        heavy.Summary!.TerminalShareOfEvPct.Should().BeGreaterThan(75m);
        heavy.Flags.Should().Contain(DcfFlags.TerminalHeavy);
    }

    [Fact]
    public async Task A_stale_annual_period_uses_trailing_twelve_month_revenue_as_the_base_year()
    {
        var inputs = new DcfInputs(History(), TtmRevenue: 1300m, LatestQuarterEnd: new DateOnly(2026, 9, 30));

        var r = await Run(inputs: inputs);

        r.Flags.Should().Contain(DcfFlags.BaseYearTtm);
        r.Assumptions!.BaseRevenue.Should().Be(1300m);

        var fresh = await Run(inputs: new DcfInputs(History(), 1300m, new DateOnly(2026, 2, 28)));
        fresh.Flags.Should().NotContain(DcfFlags.BaseYearTtm, "only 2 months past the annual period");
        fresh.Assumptions!.BaseRevenue.Should().Be(1200m);
    }

    // --- overrides ---

    [Fact]
    public async Task Overrides_replace_the_assumptions_and_are_echoed_back()
    {
        var r = await Run(query: new GetDcfValuationQuery(
            "TGT", Growth: 0.12m, OperatingMargin: 0.18m, Wacc: 0.095m, TerminalGrowth: 0.03m, Years: 7));

        var a = r.Assumptions!;
        a.Wacc.Should().Be(0.095m);
        a.TerminalGrowth.Should().Be(0.03m);
        a.OperatingMargin.Should().Be(0.18m);
        a.Years.Should().Be(7);
        a.GrowthPath.Should().HaveCount(7);
        a.GrowthPath[0].Should().Be(0.12m);
        a.GrowthPath[^1].Should().Be(0.03m);
        r.Flags.Should().Contain(DcfFlags.OverridesApplied);
        r.Sensitivity!.GrowthByMargin.Cols[2].Should().Be(0.18m);
        r.Sensitivity.GrowthByMargin.Values[2][2].Should().Be(r.Summary!.ValuePerShare);
    }

    [Theory]
    [InlineData(8.0, null, null, null, null)]
    [InlineData(null, 0.0, null, null, null)]
    [InlineData(null, 5.0, null, null, null)]
    [InlineData(null, null, 9.0, null, null)]
    [InlineData(null, null, null, 2.5, null)]
    [InlineData(null, null, null, null, 2)]
    [InlineData(null, null, null, null, 11)]
    public async Task An_override_outside_its_range_is_invalid_not_clamped(
        double? growth, double? margin, double? wacc, double? terminalGrowth, int? years)
    {
        var r = await Run(query: new GetDcfValuationQuery(
            "TGT", (decimal?)growth, (decimal?)margin, (decimal?)wacc, (decimal?)terminalGrowth, years));

        r.NotEvaluableReason.Should().Be(DcfReasons.InvalidOverride);
        r.Detail.Should().NotBeNullOrEmpty();
        NoNumbers(r);
    }

    // --- exit-multiple cross-check ---

    [Fact]
    public async Task Exit_multiple_cross_check_uses_the_peer_median_ev_to_ebitda()
    {
        var r = await Run(peers: FourPeers());

        // peers at 8x, 10x, 14x, 16x EBITDA -> median 12x
        var x = r.ExitMultipleCrossCheck!;
        x.EvToEbitda.Should().Be(12m);
        x.UnavailableReason.Should().BeNull();

        var a = r.Assumptions!;
        var drivers = new DcfMath.Drivers(
            a.BaseRevenue, a.OperatingMargin, a.TaxRate, a.DepreciationPct, a.CapexPct, a.NwcPctOfRevenueChange);
        var ev = DcfMath.EnterpriseValueAtExitMultiple(new DcfMath.Scenario(drivers, a.GrowthPath, a.Wacc, a.TerminalGrowth), 12m)!.Value;
        x.ValuePerShare.Should().BeApproximately(CompsMath.PerShareFromEnterpriseValue(ev, 300m, 100m)!.Value, 0.05m);
        r.Sources.Should().Contain("yahoo:recommendationsbysymbol");
        r.Flags.Should().NotContain(DcfFlags.ExitMultipleUnavailable);
    }

    [Fact]
    public async Task Exit_multiple_is_unavailable_with_a_reason_when_comps_has_too_few_peers()
    {
        var r = await Run(peers: [Peer("P1", 8m), Peer("P2", 10m)]);

        r.NotEvaluableReason.Should().BeNull("the cross-check is secondary: the headline stands");
        r.Summary.Should().NotBeNull();
        r.ExitMultipleCrossCheck!.EvToEbitda.Should().BeNull();
        r.ExitMultipleCrossCheck.ValuePerShare.Should().BeNull();
        r.ExitMultipleCrossCheck.UnavailableReason.Should().Be(CompsReasons.InsufficientPeers);
        r.Flags.Should().Contain(DcfFlags.ExitMultipleUnavailable);
    }

    // --- the short reply ---

    [Fact]
    public async Task The_default_reply_fits_in_summary_plus_flags()
    {
        var r = await Run(peers: FourPeers());

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(r, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().Equal(
            "ticker", "notApplicable", "name", "currency", "notEvaluableReason", "detail", "summary", "assumptions",
            "sensitivity", "exitMultipleCrossCheck", "flags", "sources", "retrievedAt");
        root.GetProperty("summary").EnumerateObject().Select(p => p.Name).Should().Equal(
            "valuePerShare", "price", "upsidePct", "rangeLow", "rangeHigh", "impliedGrowthPct", "historicalCagrPct",
            "terminalShareOfEvPct");
        root.GetProperty("sensitivity").EnumerateObject().Select(p => p.Name).Should().Equal("waccByGrowth", "growthByMargin");
        root.GetProperty("sensitivity").GetProperty("waccByGrowth").EnumerateObject().Select(p => p.Name).Should().Equal(
            "rowAxis", "colAxis", "rows", "cols", "values");
        root.GetProperty("flags").ValueKind.Should().Be(JsonValueKind.Array);
    }
}

namespace FinanceSentry.Modules.Research.Tests.Valuation;

using System.Text.Json;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Valuation;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Xunit;

/// <summary>
/// Trading-comps compose logic (#1269): peer set, per-multiple exclusions, P/E outlier, the three-peer
/// floor, quartiles and implied value through the net-debt bridge, the not-applicable / not-evaluable paths,
/// and the short-reply shape. Worked example: the target has EV 1,200, EBITDA 100 (12x), debt 300, cash 100
/// and 10 shares, so a peer multiple m implies (100m - 200) / 10 per share.
/// </summary>
public sealed class GetCompsValuationQueryTests
{
    private const decimal TargetPrice = 100m;

    private static ValuationCurrentMetrics Company(
        string ticker,
        decimal? ev = 1200m,
        decimal? ebitda = 100m,
        decimal? revenue = 400m,
        decimal? trailingPe = 20m,
        decimal? forwardPe = 18m,
        decimal? growth = 0.10m,
        decimal? margin = 0.25m,
        decimal? debt = 300m,
        decimal? cash = 100m,
        decimal? shares = 10m,
        decimal? eps = 5m,
        decimal? pb = null,
        string industry = "Restaurants",
        string? currency = "USD",
        string? financialCurrency = "USD",
        decimal price = TargetPrice,
        decimal? marketCap = null) => new(
        ticker, price, trailingPe, forwardPe, EvToEbitda: null, DividendYield: null, ConsensusTarget: null,
        IsStale: false, NotApplicable: false, "Consumer Cyclical", industry, Name: ticker,
        Currency: currency, MarketCap: marketCap,
        Comps: new ValuationCompsInputs(
            ev, ebitda, revenue, growth, margin, pb, eps, BookValuePerShare: null, debt, cash, shares, financialCurrency));

    // A peer valued at `multiple` x its 100 EBITDA.
    private static ValuationCurrentMetrics PeerAt(string ticker, decimal multiple, decimal pe = 20m, decimal growth = 0.10m, decimal margin = 0.25m)
        => Company(ticker, ev: multiple * 100m, trailingPe: pe, growth: growth, margin: margin);

    private static FakeValuationDataService Seed(ValuationCurrentMetrics target, params ValuationCurrentMetrics[] peers)
    {
        var data = new FakeValuationDataService();
        data.Metrics[target.Ticker] = target;
        foreach (var peer in peers)
        {
            data.Metrics[peer.Ticker] = peer;
            data.DefaultPeers.Add(peer.Ticker);
        }

        return data;
    }

    private static Task<CompsValuationResult> Run(FakeValuationDataService data, string ticker = "TGT", string[]? peers = null)
        => new GetCompsValuationQueryHandler(data).Handle(new GetCompsValuationQuery(ticker, peers), default);

    private static FakeValuationDataService FourPeers() => Seed(
        Company("TGT"),
        PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

    [Fact]
    public async Task Worked_example_quartiles_rank_and_implied_value_through_the_net_debt_bridge()
    {
        var result = await Run(FourPeers());

        var m = result.Multiples!.EvToEbitda;
        m.Target.Should().Be(12m);
        (m.Min, m.P25, m.Median, m.P75, m.Max).Should().Be((8m, 9.5m, 12m, 14.5m, 16m));
        m.PercentileRank.Should().Be(50m);
        m.NotEvaluable.Should().BeFalse();
        // (9.5 x 100 - 200) / 10, (12 x 100 - 200) / 10, (14.5 x 100 - 200) / 10
        (m.ImpliedPerShare!.P25, m.ImpliedPerShare.Median, m.ImpliedPerShare.P75).Should().Be((75m, 100m, 125m));

        result.Summary!.ImpliedValueBasis.Should().Be("evToEbitda");
        result.Summary.ImpliedValueMedian.Should().Be(100m);
        result.Summary.ImpliedUpsidePct.Should().Be(0m);
        result.Summary.EvToEbitdaPercentile.Should().Be(50m);
        result.Summary.Price.Should().Be(TargetPrice);
    }

    [Fact]
    public async Task Pe_implied_value_scales_the_price_by_the_peer_multiple()
    {
        // Trailing P/E: target 20, peers 10 / 20 / 30 / 40 -> median 25 -> price x 25 / 20.
        var data = Seed(
            Company("TGT"),
            PeerAt("P1", 8m, pe: 10m), PeerAt("P2", 10m, pe: 20m), PeerAt("P3", 14m, pe: 30m), PeerAt("P4", 16m, pe: 40m));

        var pe = (await Run(data)).Multiples!.TrailingPe;

        pe.Median.Should().Be(25m);
        pe.PercentileRank.Should().Be(38m, "(1 below + half of 1 tied) / 4 = 37.5, rounded");
        pe.ImpliedPerShare!.Median.Should().Be(125m);
    }

    [Fact]
    public async Task Negative_ebitda_peer_is_left_out_of_that_multiple_only()
    {
        var data = Seed(
            Company("TGT"),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m),
            Company("LOSS", ebitda: -50m, trailingPe: 25m));

        var result = await Run(data);

        var ev = result.Multiples!.EvToEbitda;
        ev.UsableCount.Should().Be(4);
        ev.Excluded.Should().ContainSingle().Which.Should().Be(new CompsExclusion("LOSS", CompsReasons.NegativeEbitda));
        result.Multiples.TrailingPe.UsableCount.Should().Be(5, "the same peer still counts on P/E");
        result.PeerSet!.Used.Select(p => p.Ticker).Should().Contain("LOSS");
    }

    [Fact]
    public async Task Negative_earnings_peer_is_left_out_of_the_pe_multiples_only()
    {
        var data = Seed(
            Company("TGT"),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m),
            Company("LOSS", trailingPe: null, forwardPe: null, eps: -2m));

        var result = await Run(data);

        result.Multiples!.TrailingPe.Excluded.Should().ContainSingle()
            .Which.Should().Be(new CompsExclusion("LOSS", CompsReasons.NegativeEarnings));
        result.Multiples.EvToEbitda.UsableCount.Should().Be(5);
    }

    [Fact]
    public async Task Pe_above_100_is_an_outlier_flagged_and_kept_out_of_the_statistics()
    {
        var data = Seed(
            Company("TGT"),
            PeerAt("P1", 8m, pe: 10m), PeerAt("P2", 10m, pe: 20m), PeerAt("P3", 14m, pe: 30m), PeerAt("P4", 16m, pe: 40m),
            PeerAt("HYPE", 12m, pe: 150m));

        var result = await Run(data);

        var pe = result.Multiples!.TrailingPe;
        pe.Max.Should().Be(40m);
        pe.UsableCount.Should().Be(4);
        pe.Excluded.Should().ContainSingle().Which.Should().Be(new CompsExclusion("HYPE", CompsReasons.PeOutlier));
        result.Flags.Should().Contain(CompsFlags.PeOutlierExcluded);
    }

    [Fact]
    public async Task Fewer_than_three_usable_peers_makes_the_multiple_not_evaluable_with_a_reason()
    {
        var data = Seed(
            Company("TGT"),
            PeerAt("P1", 8m), PeerAt("P2", 10m), Company("LOSS", ebitda: -5m));

        var result = await Run(data);

        var ev = result.Multiples!.EvToEbitda;
        ev.NotEvaluable.Should().BeTrue();
        ev.NotEvaluableReason.Should().Be(CompsReasons.InsufficientPeers);
        ev.UsableCount.Should().Be(2);
        (ev.Min, ev.P25, ev.Median, ev.P75, ev.Max, ev.PercentileRank, ev.ImpliedPerShare).Should().Be((null, null, null, null, null, null, null));
        result.Multiples.TrailingPe.NotEvaluable.Should().BeFalse("three peers still have a P/E");
        result.Flags.Should().Contain(CompsFlags.MultipleNotEvaluable);
        result.Summary!.EvToEbitdaPercentile.Should().BeNull();
    }

    [Fact]
    public async Task Non_equity_and_unreachable_peers_are_dropped_with_a_reason()
    {
        var data = FourPeers();
        data.Metrics["COIN"] = new ValuationCurrentMetrics(
            "COIN", null, null, null, null, null, null, IsStale: false, NotApplicable: true, null, null);
        data.DefaultPeers.AddRange(["COIN", "GONE"]);

        var result = await Run(data);

        result.PeerSet!.Dropped.Should().BeEquivalentTo(
            [new CompsPeerDropped("COIN", CompsReasons.NotEquity), new CompsPeerDropped("GONE", CompsReasons.FetchFailed)]);
        result.PeerSet.Used.Should().HaveCount(4);
        result.Flags.Should().Contain(CompsFlags.PeersDropped);
    }

    [Fact]
    public async Task Peer_in_another_industry_is_flagged()
    {
        var data = Seed(
            Company("TGT"),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m),
            Company("OTHER", industry: "Semiconductors"));

        var result = await Run(data);

        result.PeerSet!.Used.Single(p => p.Ticker == "OTHER").IndustryMatch.Should().BeFalse();
        result.PeerSet.Used.Single(p => p.Ticker == "P1").IndustryMatch.Should().BeTrue();
        result.Flags.Should().Contain(CompsFlags.PeerIndustryMismatch);
    }

    [Fact]
    public async Task Peer_set_is_the_snapshots_default_capped_at_six_and_overridable()
    {
        var data = Seed(Company("TGT"), Enumerable.Range(1, 8).Select(i => PeerAt($"P{i}", 8m + i)).ToArray());

        var byDefault = await Run(data);
        var custom = await Run(data, peers: ["p2", "TGT", "p3", "P2", "p4"]);

        byDefault.PeerSet!.Name.Should().Be("sector:Consumer Cyclical (default)");
        byDefault.PeerSet.Used.Should().HaveCount(6);
        custom.PeerSet!.Name.Should().Be("custom");
        custom.PeerSet.Used.Select(p => p.Ticker).Should().Equal("P2", "P3", "P4");
    }

    [Fact]
    public async Task Growth_and_margin_get_statistics_and_a_rank_but_no_implied_value()
    {
        var data = Seed(
            Company("TGT", growth: 0.10m, margin: 0.25m),
            PeerAt("P1", 8m, growth: 0.02m, margin: 0.10m), PeerAt("P2", 10m, growth: 0.06m, margin: 0.20m),
            PeerAt("P3", 14m, growth: 0.12m, margin: 0.30m), PeerAt("P4", 16m, growth: 0.20m, margin: 0.40m));

        var result = await Run(data);

        result.Growth!.Target.Should().Be(10m);
        result.Growth.Median.Should().Be(9m);
        result.Growth.PercentileRank.Should().Be(50m);
        result.Growth.ImpliedPerShare.Should().BeNull();
        result.Margin!.PercentileRank.Should().Be(50m);
        result.Summary!.GrowthPercentile.Should().Be(50m);
        result.Summary.MarginPercentile.Should().Be(50m);
    }

    [Fact]
    public async Task Negative_ebitda_target_falls_back_to_ev_revenue()
    {
        var data = Seed(
            Company("TGT", ebitda: -20m, ev: 800m, revenue: 400m),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        var ev = result.Multiples!.EvToEbitda;
        ev.NotEvaluable.Should().BeTrue();
        ev.NotEvaluableReason.Should().Be("TARGET_" + CompsReasons.NegativeEbitda);
        ev.Target.Should().BeNull();
        ev.Median.Should().Be(12m, "the peer statistics stay for the follow-up question");
        ev.ImpliedPerShare.Should().BeNull();
        result.Multiples.EvToRevenue.Target.Should().Be(2m);
        result.Flags.Should().Contain(CompsFlags.EvRevenueFallback);
        result.Summary!.EvToEbitdaPercentile.Should().BeNull();
    }

    [Fact]
    public async Task Missing_target_ebitda_is_not_an_ev_revenue_fallback()
    {
        var data = Seed(
            Company("TGT", ebitda: null),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        result.Multiples!.EvToEbitda.NotEvaluable.Should().BeTrue();
        result.Multiples.EvToEbitda.NotEvaluableReason.Should().Be("TARGET_" + CompsReasons.MissingData);
        result.Flags.Should().NotContain(CompsFlags.EvRevenueFallback);
    }

    [Fact]
    public async Task Missing_peer_data_is_null_never_zero()
    {
        var bare = new ValuationCurrentMetrics(
            "BARE", 50m, null, null, null, null, null, IsStale: false, NotApplicable: false, "Consumer Cyclical", "Restaurants");
        var data = Seed(Company("TGT"), bare);
        data.Metrics["B2"] = bare with { Ticker = "B2" };
        data.Metrics["B3"] = bare with { Ticker = "B3" };
        data.DefaultPeers.AddRange(["B2", "B3"]);

        var result = await Run(data);

        foreach (var measure in new[] { result.Multiples!.EvToEbitda, result.Multiples.EvToRevenue, result.Multiples.TrailingPe, result.Growth!, result.Margin! })
        {
            measure.NotEvaluable.Should().BeTrue();
            measure.Median.Should().BeNull();
            measure.Excluded.Should().HaveCount(3).And.OnlyContain(e => e.Reason == CompsReasons.MissingData);
        }

        result.Summary!.ImpliedValueMedian.Should().BeNull();
        result.Summary.ImpliedValueBasis.Should().BeNull();
    }

    [Fact]
    public async Task Missing_net_debt_leaves_the_ev_implied_value_null_and_flags_it()
    {
        var data = Seed(
            Company("TGT", debt: null),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        result.Multiples!.EvToEbitda.ImpliedPerShare.Should().BeNull();
        result.Multiples.EvToEbitda.Median.Should().Be(12m);
        result.Flags.Should().Contain(CompsFlags.NetDebtUnavailable);
        result.Summary!.ImpliedValueBasis.Should().Be("trailingPe", "the first multiple that can be bridged");
    }

    [Fact]
    public async Task Missing_share_count_falls_back_to_market_cap_over_price_and_says_so()
    {
        var data = Seed(
            Company("TGT", shares: null, marketCap: 1000m),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        result.Multiples!.EvToEbitda.ImpliedPerShare!.Median.Should().Be(100m);
        result.Flags.Should().Contain(CompsFlags.SharesFromMarketCap);
    }

    [Fact]
    public async Task Reporting_currency_different_from_quote_currency_is_converted_and_flagged()
    {
        var data = Seed(
            Company("TGT", financialCurrency: "EUR", currency: "USD"),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        var expected = Math.Round(100m * CurrencyConverter.ToUsd(1m, "EUR") / CurrencyConverter.ToUsd(1m, "USD"), 2);
        result.Multiples!.EvToEbitda.ImpliedPerShare!.Median.Should().Be(expected);
        result.Flags.Should().Contain(CompsFlags.FxConverted);
    }

    [Fact]
    public async Task Unknown_reporting_currency_leaves_the_ev_implied_value_null()
    {
        var data = Seed(
            Company("TGT", financialCurrency: "XXX"),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        result.Multiples!.EvToEbitda.ImpliedPerShare.Should().BeNull();
        result.Flags.Should().Contain(CompsFlags.FxUnknown);
    }

    [Fact]
    public async Task Pence_quoted_price_gets_the_implied_value_in_pence()
    {
        var data = Seed(
            Company("TGT", currency: "GBp", financialCurrency: "GBP", price: 10000m),
            PeerAt("P1", 8m), PeerAt("P2", 10m), PeerAt("P3", 14m), PeerAt("P4", 16m));

        var result = await Run(data);

        result.Multiples!.EvToEbitda.ImpliedPerShare!.Median.Should().Be(10000m, "100 GBP a share is 10,000 GBp");
        result.Flags.Should().NotContain(CompsFlags.FxConverted);
    }

    [Fact]
    public async Task Bank_runs_pe_and_pb_and_refuses_ev_multiples()
    {
        var bank = Company("BANK", industry: "Banks - Regional", pb: 1.2m);
        var data = Seed(
            bank,
            Company("B1", industry: "Banks - Regional", pb: 0.8m, trailingPe: 8m),
            Company("B2", industry: "Banks - Regional", pb: 1.0m, trailingPe: 10m),
            Company("B3", industry: "Banks - Regional", pb: 1.4m, trailingPe: 12m),
            Company("B4", industry: "Banks - Regional", pb: 1.6m, trailingPe: 14m));

        var result = await Run(data, "BANK");

        result.Flags.Should().Contain(CompsFlags.FinancialSector);
        result.Multiples!.EvToEbitda.NotEvaluableReason.Should().Be(CompsReasons.FinancialSector);
        result.Multiples.EvToRevenue.NotEvaluableReason.Should().Be(CompsReasons.FinancialSector);
        result.Multiples.EvToEbitda.Median.Should().BeNull();
        result.Multiples.PriceToBook!.Median.Should().Be(1.2m);
        result.Multiples.PriceToBook.ImpliedPerShare!.Median.Should().Be(100m);
        result.Multiples.TrailingPe.Median.Should().Be(11m);
        result.Summary!.ImpliedValueBasis.Should().Be("trailingPe");
        result.Flags.Should().NotContain(CompsFlags.NetDebtUnavailable);
    }

    [Theory]
    [InlineData("Insurance - Life", true)]
    [InlineData("Insurance - Property & Casualty", true)]
    [InlineData("Insurance Brokers", false)]
    public async Task Only_insurers_not_brokers_are_financial_sector(string industry, bool financial)
    {
        var data = Seed(
            Company("INS", industry: industry),
            Company("I1", industry: industry), Company("I2", industry: industry),
            Company("I3", industry: industry), Company("I4", industry: industry));

        var result = await Run(data, "INS");

        result.Flags.Contains(CompsFlags.FinancialSector).Should().Be(financial);
        result.Multiples!.EvToEbitda.NotEvaluable.Should().Be(financial);
    }

    [Fact]
    public async Task Non_financial_target_has_no_price_to_book_measure()
    {
        (await Run(FourPeers())).Multiples!.PriceToBook.Should().BeNull();
    }

    [Fact]
    public async Task Crypto_is_not_applicable_with_no_numbers()
    {
        var data = new FakeValuationDataService();
        data.Metrics["SOL-USD"] = new ValuationCurrentMetrics(
            "SOL-USD", null, null, null, null, null, null, IsStale: false, NotApplicable: true, null, null);

        var result = await Run(data, "sol-usd");

        result.NotApplicable.Should().BeTrue();
        result.Ticker.Should().Be("SOL-USD");
        (result.Summary, result.Multiples, result.PeerSet, result.Growth, result.Margin).Should().Be((null, null, null, null, null));
    }

    [Fact]
    public async Task Source_failure_is_data_unavailable_not_a_fabricated_result()
    {
        var result = await Run(new FakeValuationDataService(), "ZZZZ");

        result.NotApplicable.Should().BeFalse();
        result.NotEvaluableReason.Should().Be(CompsReasons.DataUnavailable);
        result.Summary.Should().BeNull();
    }

    [Fact]
    public async Task Default_reply_is_a_summary_plus_flags_with_a_pinned_shape()
    {
        var result = await Run(FourPeers());

        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.EnumerateObject().Select(p => p.Name).Should().Equal(
            "ticker", "notApplicable", "name", "sector", "industry", "currency", "notEvaluableReason",
            "peerSet", "summary", "multiples", "growth", "margin", "flags", "sources", "retrievedAt");
        json.GetProperty("summary").EnumerateObject().Select(p => p.Name).Should().Equal(
            "evToEbitdaPercentile", "evToRevenuePercentile", "trailingPePercentile", "priceToBookPercentile",
            "growthPercentile", "marginPercentile", "impliedValueBasis", "impliedValueMedian", "impliedUpsidePct", "price");
        json.GetProperty("flags").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("multiples").GetProperty("evToEbitda").EnumerateObject().Select(p => p.Name).Should().Contain(
            ["target", "min", "p25", "median", "p75", "max", "percentileRank", "impliedPerShare", "excluded"]);
    }
}

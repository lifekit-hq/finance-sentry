namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.Domain.Dossier;
using FluentAssertions;
using Xunit;

public class DossierChecksBuilderTests
{
    private static readonly DossierCheckInputs Empty = new(
        IsCrypto: false,
        BuyRatings: null,
        TotalRatings: null,
        ImpliedUpsidePct: null,
        RevenueYoyPct: null,
        EpsYoyPct: null,
        TrailingPe: null,
        TrailingPeFiveYearAvg: null,
        ForwardPe: null,
        PeerForwardPes: [],
        YearCloses: [],
        YearChangePct: null,
        BenchmarkYearChangePct: null,
        IsHeld: false,
        ShareOfBookPercent: null,
        PositionCapPercent: null,
        HasThesis: false,
        ThesisBroken: false);

    [Fact]
    public void Stock_HasFiveAxesOfTwoRules()
    {
        var checks = DossierChecksBuilder.Build(Empty);

        checks.Axes.Select(a => a.Key).Should().Equal("analysts", "growth", "value", "momentum", "fit");
        checks.Axes.Should().OnlyContain(a => a.Rules.Count == 2);
    }

    [Fact]
    public void Crypto_LeavesOutTheAxesThatDoNotApply()
    {
        var checks = DossierChecksBuilder.Build(Empty with { IsCrypto = true });

        checks.Axes.Select(a => a.Key).Should().Equal("momentum", "fit");
    }

    [Fact]
    public void MissingFacts_AreUnevaluatedWithAReasonNotFailed()
    {
        var checks = DossierChecksBuilder.Build(Empty);

        var unevaluated = checks.Axes.SelectMany(a => a.Rules).Where(r => r.Key != "has_thesis").ToList();
        unevaluated.Should().OnlyContain(r => r.Passed == null && r.Note != null);
        checks.Axes.Where(a => a.Key != "fit").Should().OnlyContain(a => a.Evaluated == 0 && a.Passed == 0);
    }

    [Fact]
    public void Analysts_BuyShareAndUpside()
    {
        var axis = Axis(Empty with { BuyRatings = 12, TotalRatings = 20, ImpliedUpsidePct = 8m }, "analysts");

        var buyShare = axis.Rules.Single(r => r.Key == "buy_share");
        buyShare.Passed.Should().BeTrue();
        buyShare.Value.Should().Be(60m);
        buyShare.Note.Should().Be("12 of 20 rate it Buy or Strong Buy");
        axis.Rules.Single(r => r.Key == "target_upside").Passed.Should().BeFalse();
        axis.Passed.Should().Be(1);
        axis.Evaluated.Should().Be(2);
    }

    [Fact]
    public void Growth_PositiveYearOverYear()
    {
        var axis = Axis(Empty with { RevenueYoyPct = 4.2m, EpsYoyPct = -1m }, "growth");

        axis.Rules.Select(r => r.Passed).Should().Equal(true, false);
    }

    [Fact]
    public void Value_PeBelowHistoryAndForwardAtOrBelowPeerMedian()
    {
        var axis = Axis(
            Empty with { TrailingPe = 20m, TrailingPeFiveYearAvg = 25m, ForwardPe = 18m, PeerForwardPes = [30m, 16m, 18m, -4m] },
            "value");

        axis.Rules.Single(r => r.Key == "pe_vs_history").Passed.Should().BeTrue();
        var peers = axis.Rules.Single(r => r.Key == "forward_pe_vs_peers");
        peers.Threshold.Should().Be(18m, "median of the positive peers 16, 18, 30");
        peers.Passed.Should().BeTrue();
        peers.Note.Should().Be("median of 3 peers");
    }

    [Fact]
    public void Value_EvenPeerCount_TakesTheMeanOfTheMiddleTwo()
    {
        var axis = Axis(Empty with { ForwardPe = 21m, PeerForwardPes = [10m, 20m, 22m, 40m] }, "value");

        var peers = axis.Rules.Single(r => r.Key == "forward_pe_vs_peers");
        peers.Threshold.Should().Be(21m);
        peers.Passed.Should().BeTrue();
    }

    [Fact]
    public void Value_NoHistory_KeepsTheValueButDoesNotScore()
    {
        var rule = Axis(Empty with { TrailingPe = 20m }, "value").Rules.Single(r => r.Key == "pe_vs_history");

        rule.Passed.Should().BeNull();
        rule.Value.Should().Be(20m);
    }

    [Fact]
    public void Momentum_NeedsTwoHundredClosesAndTheBenchmark()
    {
        var closes = Enumerable.Repeat(100m, 199).Append(110m).ToList();

        var axis = Axis(Empty with { YearCloses = closes, YearChangePct = 12m, BenchmarkYearChangePct = 15m }, "momentum");

        var above = axis.Rules.Single(r => r.Key == "above_200d");
        above.Passed.Should().BeTrue();
        above.Value.Should().Be(9.9m);
        axis.Rules.Single(r => r.Key == "beats_sp500").Passed.Should().BeFalse();
    }

    [Fact]
    public void Momentum_TooFewCloses_IsUnevaluated()
    {
        var rule = Axis(Empty with { YearCloses = [100m, 110m] }, "momentum").Rules.Single(r => r.Key == "above_200d");

        rule.Passed.Should().BeNull();
        rule.Note.Should().Be("fewer than 200 daily closes");
    }

    [Theory]
    [InlineData(12.2, 10, false)]
    [InlineData(8, 10, true)]
    [InlineData(10, 10, true)]
    public void Fit_ShareOfBookAgainstTheCap(double share, double cap, bool passed)
    {
        var rule = Axis(Empty with { IsHeld = true, ShareOfBookPercent = (decimal)share, PositionCapPercent = (decimal)cap }, "fit")
            .Rules.Single(r => r.Key == "within_cap");

        rule.Passed.Should().Be(passed);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Fit_ThesisWrittenAndNotBroken(bool hasThesis, bool broken, bool passed)
    {
        var rule = Axis(Empty with { IsHeld = true, HasThesis = hasThesis, ThesisBroken = broken }, "fit")
            .Rules.Single(r => r.Key == "has_thesis");

        rule.Passed.Should().Be(passed);
    }

    [Fact]
    public void Fit_ThesisOnAnAssetNotHeld_IsUnevaluated()
    {
        var axis = Axis(Empty with { IsHeld = false, HasThesis = false }, "fit");
        var rule = axis.Rules.Single(r => r.Key == "has_thesis");

        rule.Passed.Should().BeNull();
        rule.Note.Should().Be("not held");
        axis.Evaluated.Should().Be(0);
    }

    private static DossierCheckAxis Axis(DossierCheckInputs inputs, string key)
        => DossierChecksBuilder.Build(inputs).Axes.Single(a => a.Key == key);
}

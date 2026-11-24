using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.Radar;

public sealed class ConcentrationHistoryCalculatorTests
{
    private static ConcentrationCapture Capture(string day, params ConcentrationPosition[] positions)
        => new(DateTimeOffset.Parse(day + "T06:00:00Z"), positions);

    private static ConcentrationPosition Stock(string symbol, decimal usd) => new(symbol, false, usd);

    private static ConcentrationPosition Coin(string symbol, decimal usd) => new(symbol, true, usd);

    private static ConcentrationHistoryInputs Inputs(
        IReadOnlyList<ConcentrationCapture> captures,
        IReadOnlyList<ConcentrationCash>? cash = null,
        IReadOnlyList<ConcentrationTarget>? targets = null)
        => new(captures, cash ?? [], targets ?? []);

    [Fact]
    public void Build_SeriesWithDrift_ReportsTopThreeAndCryptoShareEachDayAgainstTheTargets()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
            [
                Capture("2026-01-05", Stock("A", 30), Stock("B", 30), Stock("C", 20), Stock("D", 10), Coin("BTC", 10)),
                Capture("2026-06-05", Stock("A", 50), Stock("B", 20), Stock("C", 10), Stock("D", 5), Coin("BTC", 15)),
            ],
            [new ConcentrationCash(new DateOnly(2026, 1, 5), 10, 100), new ConcentrationCash(new DateOnly(2026, 6, 5), 30, 100)],
            [new ConcentrationTarget(AssetClassNormalizer.Crypto, 0.10m), new ConcentrationTarget(AssetClassNormalizer.Cash, 0.05m)]));

        result.Reason.Should().BeNull();
        result.Points.Should().HaveCount(2);
        result.Points[0].Top3Share.Should().Be(0.8m);
        result.Points[0].CryptoShare.Should().Be(0.1m);
        result.Points[0].CashShare.Should().Be(0.1m);
        result.Points[1].Top3Share.Should().Be(0.85m);
        result.Points[1].CryptoShare.Should().Be(0.15m);
        result.Points[1].CashShare.Should().Be(0.3m);
        result.CryptoTarget.Should().Be(0.10m);
        result.CashTarget.Should().Be(0.05m);
    }

    [Fact]
    public void Build_DriftingTopThree_RisesAsTheLargestPositionGrows()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
        [
            Capture("2026-01-05", Stock("A", 25), Stock("B", 25), Stock("C", 25), Stock("D", 25)),
            Capture("2026-03-05", Stock("A", 60), Stock("B", 20), Stock("C", 10), Stock("D", 10)),
        ]));

        result.Points.Select(p => p.Top3Share).Should().Equal(0.75m, 0.9m);
    }

    [Fact]
    public void Build_FlatSeries_ReturnsTheSameSharesEveryDay()
    {
        var day = new[] { Stock("A", 40), Stock("B", 30), Stock("C", 20), Coin("ETH", 10) };
        var result = ConcentrationHistoryCalculator.Build(Inputs(
            [Capture("2026-01-05", day), Capture("2026-02-05", day), Capture("2026-03-05", day)]));

        result.Reason.Should().BeNull();
        result.Points.Should().HaveCount(3);
        result.Points.Select(p => p.Top3Share).Distinct().Should().ContainSingle().Which.Should().Be(0.9m);
        result.Points.Select(p => p.CryptoShare).Distinct().Should().ContainSingle().Which.Should().Be(0.1m);
        result.Points.Should().OnlyContain(p => p.CashShare == null);
        result.CryptoTarget.Should().BeNull();
    }

    [Fact]
    public void Build_OneDayOfHistory_IsTooLittleToDraw()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
            [Capture("2026-01-05", Stock("A", 50), Stock("B", 50))],
            targets: [new ConcentrationTarget(AssetClassNormalizer.Crypto, 0.1m)]));

        result.Points.Should().BeEmpty();
        result.Reason.Should().Be(ConcentrationHistoryCalculator.NotEnoughHistory);
        result.CryptoTarget.Should().Be(0.1m);
    }

    [Fact]
    public void Build_NoCaptures_IsTooLittleHistory()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs([]));

        result.Points.Should().BeEmpty();
        result.Reason.Should().Be(ConcentrationHistoryCalculator.NotEnoughHistory);
    }

    [Fact]
    public void Build_UnpricedDay_IsSkipped()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
        [
            Capture("2026-01-05", Stock("A", 0), Stock("B", -1)),
            Capture("2026-02-05", Stock("A", 50), Stock("B", 50)),
            Capture("2026-03-05", Stock("A", 50), Stock("B", 50)),
        ]));

        result.Points.Select(p => p.Date).Should().Equal(new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 5));
    }

    [Fact]
    public void Build_SameDayCaptures_KeepOnlyTheLatestWhateverTheInputOrder()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
        [
            new ConcentrationCapture(DateTimeOffset.Parse("2026-02-05T09:00:00Z"), [Stock("A", 50), Coin("BTC", 50)]),
            new ConcentrationCapture(DateTimeOffset.Parse("2026-02-05T01:00:00Z"), [Stock("A", 90), Stock("B", 10)]),
            Capture("2026-03-05", Stock("A", 50), Stock("B", 50)),
        ]));

        result.Points.Should().HaveCount(2);
        result.Points[0].Date.Should().Be(new DateOnly(2026, 2, 5));
        result.Points[0].CryptoShare.Should().Be(0.5m);
    }

    [Fact]
    public void Build_DayWithNoSnapshotTotal_CarriesNoCashShare()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
            [Capture("2026-01-05", Stock("A", 50)), Capture("2026-01-06", Stock("A", 50))],
            [new ConcentrationCash(new DateOnly(2026, 1, 5), 0, 0), new ConcentrationCash(new DateOnly(2026, 1, 6), 20, 100)]));

        result.Points.Select(p => p.CashShare).Should().Equal(null, 0.2m);
    }

    [Fact]
    public void Build_SeveralTargetsOfOneClass_AreSummed()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
            [Capture("2026-01-05", Stock("A", 50)), Capture("2026-01-06", Stock("A", 50))],
            targets:
            [
                new ConcentrationTarget(AssetClassNormalizer.Crypto, 0.04m),
                new ConcentrationTarget(AssetClassNormalizer.Crypto, 0.06m),
            ]));

        result.CryptoTarget.Should().Be(0.10m);
    }

    [Fact]
    public void Build_SameSymbolOnTwoVenues_CountsOncePerRank()
    {
        var result = ConcentrationHistoryCalculator.Build(Inputs(
        [
            Capture("2026-01-05", Coin("BTC", 20), Coin("btc", 20), Stock("A", 20), Stock("B", 20), Stock("C", 20)),
            Capture("2026-01-06", Coin("BTC", 20), Coin("btc", 20), Stock("A", 20), Stock("B", 20), Stock("C", 20)),
        ]));

        result.Points[0].Top3Share.Should().Be(0.8m);
    }
}

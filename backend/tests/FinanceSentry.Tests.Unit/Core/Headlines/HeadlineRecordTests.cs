namespace FinanceSentry.Tests.Unit.Core.Headlines;

using System.Text.Json;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Utils;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Xunit;

/// <summary>The record's wire shape and its base-currency view (C4: compute in USD, show in the base currency).</summary>
public class HeadlineRecordTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serializes_EnumsAsTheirFrameworkNames()
    {
        var headline = HeadlineFactory.Change(
            0m, 1500m, HeadlineScope.InvestedBrokerage, Today.AddDays(-30), Today, false,
            new HeadlineSignals { StaleSources = ["ibkr"], Fx = FxStatus.Unknown1To1 });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(headline, Web));
        var root = json.RootElement;

        root.GetProperty("method").GetString().Should().Be("VALUE_CHANGE");
        root.GetProperty("basis").GetString().Should().Be("START_VALUE");
        root.GetProperty("scope").GetString().Should().Be("invested_brokerage");
        root.GetProperty("quality").GetString().Should().Be("DEGRADED");
        root.GetProperty("basisStatus").GetString().Should().Be("COMPLETE");
        root.GetProperty("percent").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("currency").GetString().Should().Be("USD");
        root.GetProperty("window").GetProperty("from").GetString().Should().Be("2026-09-15");
        root.GetProperty("reasons").EnumerateArray()
            .Select(r => r.GetProperty("code").GetString())
            .Should().Equal("STALE_SOURCE", "FX_UNKNOWN_1TO1", "ZERO_BASE");
        root.GetProperty("reasons")[0].GetProperty("source").GetString().Should().Be("ibkr");
    }

    [Fact]
    public void EveryReasonCode_HasAnUpperSnakeWireName()
    {
        foreach (var code in Enum.GetValues<HeadlineReasonCode>())
        {
            var name = JsonSerializer.Serialize(code).Trim('"');
            name.Should().MatchRegex("^[A-Z0-9]+(_[A-Z0-9]+)*$", because: $"{code} is a typed reason code");
        }
    }

    [Fact]
    public void RoundTrips()
    {
        var headline = HeadlineFactory.GainVsCost(
            [new CostedValue(600m, 500m), new CostedValue(400m, null)], HeadlineScope.Source("IBKR"), Today,
            new HeadlineSignals { Fx = FxStatus.TodayRate });

        var back = JsonSerializer.Deserialize<Headline>(JsonSerializer.Serialize(headline, Web), Web)!;

        back.Should().BeEquivalentTo(headline);
        back.Scope.Should().Be("source:ibkr");
    }

    [Fact]
    public void InBaseCurrency_TodaysValue_AtALiveRate_StaysComplete()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 1.25m });
        var usd = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, HeadlineSignals.None);

        var eur = HeadlineFactory.InBaseCurrency(usd, "EUR", Today);

        eur.Amount.Should().Be(800m);
        eur.Currency.Should().Be("EUR");
        eur.Quality.Should().Be(HeadlineQuality.Complete);
        eur.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void InBaseCurrency_APastWindow_GainsFxTodayRate_AndKeepsItsPercent()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 1.25m });
        var usd = HeadlineFactory.Change(
            1000m, 1100m, HeadlineScope.Umbrella, Today.AddDays(-30), Today, false, HeadlineSignals.None);

        var eur = HeadlineFactory.InBaseCurrency(usd, "EUR", Today);

        eur.Amount.Should().Be(80m);
        eur.Percent.Should().Be(usd.Percent);
        eur.Quality.Should().Be(HeadlineQuality.Estimated);
        eur.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.FxTodayRate));
    }

    [Fact]
    public void InBaseCurrency_KeepsAWorseCarriedReason()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 1.25m });
        var usd = HeadlineFactory.Change(
            1000m, 1100m, HeadlineScope.Umbrella, Today.AddDays(-30), Today, false,
            new HeadlineSignals { Fx = FxStatus.Unknown1To1 });

        var eur = HeadlineFactory.InBaseCurrency(usd, "EUR", Today);

        eur.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.FxUnknown1To1));
        eur.Quality.Should().Be(HeadlineQuality.Degraded);
    }

    [Fact]
    public void InBaseCurrency_ASeedRate_IsEstimated()
    {
        using var _ = FxTableScope.Seed();
        var usd = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, HeadlineSignals.None);

        var eur = HeadlineFactory.InBaseCurrency(usd, "EUR", Today);

        eur.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.FxTodayRate));
    }

    [Fact]
    public void InBaseCurrency_Usd_IsUnchanged()
    {
        var usd = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today.AddDays(-3), HeadlineSignals.None);

        HeadlineFactory.InBaseCurrency(usd, "usd", Today).Should().BeSameAs(usd);
    }

    [Fact]
    public void InBaseCurrency_AnUnavailableFigure_StaysNull()
    {
        using var _ = FxTableScope.Live(new Dictionary<string, decimal> { ["EUR"] = 1.25m });
        var usd = HeadlineFactory.Value(540m, HeadlineScope.CashSleeve, Today, new HeadlineSignals { NoSplit = true });

        var eur = HeadlineFactory.InBaseCurrency(usd, "EUR", Today);

        eur.Amount.Should().BeNull();
        eur.Quality.Should().Be(HeadlineQuality.Unavailable);
    }
}

using System.Text.Json;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

public class RevolutXHoldingsAggregatorTests
{
    private readonly RevolutXHoldingsAggregator _sut = new();

    private static RevolutXHoldingsSnapshot AggregateFixtures(decimal dust = 0.01m) =>
        new RevolutXHoldingsAggregator().Aggregate(
            JsonSerializer.Deserialize<List<RevolutXBalance>>(RevolutXFixtures.Balances)!,
            JsonSerializer.Deserialize<Dictionary<string, RevolutXCurrency>>(RevolutXFixtures.Currencies)!,
            JsonSerializer.Deserialize<RevolutXTickersResponse>(RevolutXFixtures.Tickers)!.Data,
            dust);

    [Fact]
    public void Fixtures_ProduceOnePricedRowPerAsset_CryptoAndVenueFiat()
    {
        var snapshot = AggregateFixtures();

        snapshot.Holdings.Select(h => h.Asset).Should().Equal("BTC", "ETH", "DOGE", "USDC", "EUR", "USD");
    }

    [Fact]
    public void UsdPair_IsPreferredOverAnyOtherQuote()
    {
        var btc = AggregateFixtures().Holdings.Single(h => h.Asset == "BTC");

        btc.FreeQuantity.Should().Be(0.25m);
        btc.LockedQuantity.Should().Be(0.05m);
        btc.UsdValue.Should().Be(18_000m, "0.30 BTC at the BTC/USD price, not the BTC/EUR one");
    }

    [Fact]
    public void StakedFunds_CountAsLocked_AndAFiatQuote_IsConvertedToUsdHere()
    {
        var eth = AggregateFixtures().Holdings.Single(h => h.Asset == "ETH");

        eth.FreeQuantity.Should().Be(1.5m);
        eth.LockedQuantity.Should().Be(2m, "staked sits outside total and is still the user's");
        eth.UsdValue.Should().Be(Math.Round(CurrencyConverter.ToUsd(3.5m * 3_000m, "EUR"), 4));
    }

    [Fact]
    public void EmptyLastPrice_FallsBackToMid_AndAUsdStablecoinQuoteIsAtPar()
    {
        AggregateFixtures().Holdings.Single(h => h.Asset == "DOGE").UsdValue.Should().Be(1.0m);
    }

    [Fact]
    public void UsdStablecoinWithoutItsOwnPair_IsADollar()
    {
        AggregateFixtures().Holdings.Single(h => h.Asset == "USDC").UsdValue.Should().Be(250.5m);
    }

    [Fact]
    public void FiatCash_IsKeptAsVenueFiat_NeverSilentlyDropped()
    {
        var snapshot = AggregateFixtures();

        snapshot.Holdings.Where(h => h.IsFiat).Should().BeEquivalentTo(
        [
            new CryptoAssetBalance("EUR", 1000m, 0m, Math.Round(CurrencyConverter.ToUsd(1000m, "EUR"), 4), IsFiat: true),
            new CryptoAssetBalance("USD", 12.34m, 0m, 12.34m, IsFiat: true),
        ]);
        snapshot.Holdings.Where(h => !h.IsFiat).Select(h => h.Asset).Should().Equal("BTC", "ETH", "DOGE", "USDC");
        snapshot.UnratedFiat.Should().BeEmpty();
    }

    [Fact]
    public void FiatWithNoFxRate_IsKeptAtParAndReported()
    {
        var snapshot = _sut.Aggregate(
            [new RevolutXBalance("CHF", "10", "0", null, "10")],
            new Dictionary<string, RevolutXCurrency>
            {
                ["CHF"] = new("CHF", "Swiss Franc", 2, "fiat", "active"),
            },
            [],
            0.01m);

        snapshot.Holdings.Should().ContainSingle().Which.Should().Be(new CryptoAssetBalance("CHF", 10m, 0m, 10m, IsFiat: true));
        snapshot.UnratedFiat.Should().Equal("CHF");
    }

    [Fact]
    public void AssetWithNoUsdConvertiblePair_IsReportedAsUnpriced()
    {
        AggregateFixtures().UnpricedAssets.Should().Equal("PEPE");
    }

    [Fact]
    public void DustBelowTheThreshold_IsDropped()
    {
        AggregateFixtures(dust: 5m).Holdings.Select(h => h.Asset).Should().NotContain("DOGE");
    }

    [Fact]
    public void AmountsParseWithTheInvariantCulture_WhateverTheThreadCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("uk-UA");

            var snapshot = _sut.Aggregate(
                [new RevolutXBalance("BTC", "0.12345678", "0", null, "0.12345678")],
                new Dictionary<string, RevolutXCurrency>(),
                [new RevolutXTicker("BTC/USD", null, null, null, "100000.5")],
                0.01m);

            snapshot.Holdings.Single().FreeQuantity.Should().Be(0.12345678m);
            snapshot.Holdings.Single().UsdValue.Should().Be(Math.Round(0.12345678m * 100_000.5m, 4));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CurrencyMissingFromConfiguration_WithAnFxRate_IsTreatedAsFiat()
    {
        var snapshot = _sut.Aggregate(
            [new RevolutXBalance("GBP", "10", "0", null, "10")],
            new Dictionary<string, RevolutXCurrency>(),
            [],
            0.01m);

        snapshot.Holdings.Should().ContainSingle().Which.IsFiat.Should().BeTrue();
    }

    [Fact]
    public void BalanceWithoutAvailable_FallsBackToTotal()
    {
        var snapshot = _sut.Aggregate(
            [new RevolutXBalance("BTC", null, null, null, "2")],
            new Dictionary<string, RevolutXCurrency>(),
            [new RevolutXTicker("BTC/USD", null, null, "10", null)],
            0.01m);

        snapshot.Holdings.Single().Should().BeEquivalentTo(new { Asset = "BTC", FreeQuantity = 2m, LockedQuantity = 0m, UsdValue = 20m });
    }
}

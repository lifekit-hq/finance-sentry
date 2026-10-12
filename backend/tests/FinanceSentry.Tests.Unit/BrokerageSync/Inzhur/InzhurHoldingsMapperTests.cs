using System.Text.Json;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

public class InzhurHoldingsMapperTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static InzhurPortfolio Portfolio(object assets, object? account = null)
        => new(
            JsonSerializer.Deserialize<InzhurUserAssetsResponse>(JsonSerializer.Serialize(assets), Web)!.Assets ?? [],
            account is null ? null : JsonSerializer.Deserialize<InzhurBrokerAccount>(JsonSerializer.Serialize(account), Web));

    [Fact]
    public void Fund_lands_as_REIT_with_UAH_converted_once_and_cost_per_unit()
    {
        var positions = InzhurHoldingsMapper.Map(Portfolio(InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 10m, 1_200m, 1_000m))));

        var fund = positions.Should().ContainSingle().Subject;
        fund.Symbol.Should().Be("Fund A");
        fund.InstrumentType.Should().Be(InzhurHoldingsMapper.FundInstrumentType);
        fund.Quantity.Should().Be(10m);
        fund.UsdValue.Should().Be(CurrencyConverter.ToUsd(1_200m, "UAH"));
        fund.AverageCostUsd.Should().Be(CurrencyConverter.ToUsd(1_000m, "UAH") / 10m);
    }

    [Fact]
    public void Bond_lands_as_BOND_valued_at_the_sell_price_without_a_cost()
    {
        var positions = InzhurHoldingsMapper.Map(Portfolio(InzhurFakes.UserAssets(InzhurFakes.Bond("UA0000000000", 3m, 1_050m))));

        var bond = positions.Should().ContainSingle().Subject;
        bond.Symbol.Should().Be("UA0000000000");
        bond.InstrumentType.Should().Be(InzhurHoldingsMapper.BondInstrumentType);
        bond.UsdValue.Should().Be(CurrencyConverter.ToUsd(3_150m, "UAH"));
        bond.AverageCostUsd.Should().BeNull();
    }

    [Fact]
    public void Cash_row_is_free_plus_order_blocked_UAH_and_leaves_the_bonus_out()
    {
        var positions = InzhurHoldingsMapper.Map(Portfolio(InzhurFakes.UserAssets(), InzhurFakes.BrokerAccount(500m, 100m, bonus: 999m)));

        var cash = positions.Should().ContainSingle().Subject;
        cash.Symbol.Should().Be(InzhurHoldingsMapper.CashSymbol);
        cash.InstrumentType.Should().Be(InzhurHoldingsMapper.CashInstrumentType);
        cash.Quantity.Should().Be(600m);
        cash.UsdValue.Should().Be(CurrencyConverter.ToUsd(600m, "UAH"));
    }

    [Fact]
    public void Empty_cash_and_unowned_assets_produce_no_rows()
    {
        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(new { id = 9, type = "fund", name = "Sold", details = new { certificatesOwnedQuantity = 0m } }),
            InzhurFakes.BrokerAccount(0m, 0m)));

        positions.Should().BeEmpty();
    }

    [Fact]
    public void Two_lots_of_one_asset_merge_into_one_row_with_a_weighted_cost()
    {
        var positions = InzhurHoldingsMapper.Map(Portfolio(InzhurFakes.UserAssets(
            InzhurFakes.Fund("Fund A", 10m, 1_200m, 1_000m, id: 1),
            InzhurFakes.Fund("Fund A", 30m, 3_600m, 3_300m, id: 2))));

        var fund = positions.Should().ContainSingle().Subject;
        fund.Quantity.Should().Be(40m);
        fund.UsdValue.Should().Be(CurrencyConverter.ToUsd(1_200m, "UAH") + CurrencyConverter.ToUsd(3_600m, "UAH"));
        fund.AverageCostUsd.Should().Be((CurrencyConverter.ToUsd(1_000m, "UAH") + CurrencyConverter.ToUsd(3_300m, "UAH")) / 40m);
    }

    [Fact]
    public void Payload_without_a_currency_comes_out_as_UAH_with_the_amounts_unconverted()
    {
        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 10m, 1_200m, 1_000m)),
            InzhurFakes.BrokerAccount(500m, 100m)));

        positions.Should().HaveCount(2);
        positions.Should().OnlyContain(p => p.Currency == "UAH");
        positions.Single(p => p.Symbol == "Fund A").NativeValue.Should().Be(1_200m);
        positions.Single(p => p.Symbol == InzhurHoldingsMapper.CashSymbol).NativeValue.Should().Be(600m);
    }

    [Fact]
    public void Payload_with_a_reported_currency_keeps_it_and_converts_the_USD_figure_from_it()
    {
        var fund = new
        {
            id = 1,
            type = "fund",
            name = "Fund E",
            currency = "eur",
            prices = new { sellUAH = 120m },
            details = new { certificatesOwnedQuantity = 10m, totalAmountUAH = 1_200m },
        };
        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(fund),
            new { availableBalanceUAH = 50m, blockedBalanceUAH = 0m, currency = "EUR" }));

        var asset = positions.Single(p => p.Symbol == "Fund E");
        asset.Currency.Should().Be("EUR");
        asset.NativeValue.Should().Be(1_200m);
        asset.UsdValue.Should().Be(CurrencyConverter.ToUsd(1_200m, "EUR"));
        var cash = positions.Single(p => p.Symbol == InzhurHoldingsMapper.CashSymbol);
        cash.Currency.Should().Be("EUR");
        cash.NativeValue.Should().Be(50m);
    }

    [Theory]
    [InlineData(null, "UAH")]
    [InlineData("", "UAH")]
    [InlineData("hryvnia", "UAH")]
    [InlineData(" uah ", "UAH")]
    [InlineData("USD", "USD")]
    [InlineData("PLN", "UAH")]
    public void Reported_currency_is_used_only_when_a_rate_exists_for_it(string? reported, string expected)
        => InzhurHoldingsMapper.CurrencyFor(reported).Should().Be(expected);

    [Fact]
    public void Currency_in_an_unexpected_shape_reads_as_not_reported_and_keeps_the_row_in_UAH()
    {
        var fund = new
        {
            id = 1,
            type = "fund",
            name = "Fund F",
            currency = new { code = "EUR" },
            details = new { certificatesOwnedQuantity = 10m, totalAmountUAH = 1_200m },
        };
        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(fund),
            new { availableBalanceUAH = 50m, currency = 980 }));

        positions.Should().HaveCount(2);
        positions.Should().OnlyContain(p => p.Currency == "UAH");
        positions.Single(p => p.Symbol == "Fund F").UsdValue.Should().Be(CurrencyConverter.ToUsd(1_200m, "UAH"));
    }

    [Fact]
    public void Symbol_falls_back_from_name_to_title_to_isin_to_the_id()
    {
        static InzhurAsset Asset(string? name, string? title, string? isin)
            => new(JsonSerializer.SerializeToElement(42), "fund", null, null, name, title, isin, null, null, null);

        InzhurHoldingsMapper.SymbolFor(Asset("Name", "Title", "ISIN")).Should().Be("Name");
        InzhurHoldingsMapper.SymbolFor(Asset(" ", "Title", "ISIN")).Should().Be("Title");
        InzhurHoldingsMapper.SymbolFor(Asset(null, null, "ISIN")).Should().Be("ISIN");
        InzhurHoldingsMapper.SymbolFor(Asset(null, null, null)).Should().Be("INZHUR-42");
        InzhurHoldingsMapper.SymbolFor(Asset(new string('x', 80), null, null)).Should().HaveLength(50);
    }
}

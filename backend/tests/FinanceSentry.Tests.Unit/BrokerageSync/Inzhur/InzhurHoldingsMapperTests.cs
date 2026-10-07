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

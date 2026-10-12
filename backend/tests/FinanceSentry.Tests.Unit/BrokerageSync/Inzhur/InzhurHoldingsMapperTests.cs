using System.Globalization;
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

    private static InzhurPortfolio Portfolio(object assets, object? account = null, object? catalog = null)
        => new(
            JsonSerializer.Deserialize<InzhurUserAssetsResponse>(JsonSerializer.Serialize(assets), Web)!.Assets ?? [],
            account is null ? null : JsonSerializer.Deserialize<InzhurBrokerAccount>(JsonSerializer.Serialize(account), Web),
            catalog is null ? null : JsonSerializer.Deserialize<InzhurCatalogResponse>(JsonSerializer.Serialize(catalog), Web)!.Items);

    [Theory]
    [InlineData(null, "0")]
    [InlineData("0", "0")]
    [InlineData("1", "0.01")]
    [InlineData("100", "1")]
    [InlineData("12345", "123.45")]
    [InlineData("99999999", "999999.99")]
    public void Cabinet_amounts_convert_from_kopecks_to_hryvnia(string? kopecks, string hryvnia)
        => InzhurHoldingsMapper.FromMinorUnits(kopecks is null ? null : decimal.Parse(kopecks, CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(hryvnia, CultureInfo.InvariantCulture));

    // The cabinet's own shape, as its web client reads it: no name on the user-asset row (the catalog has it), money in
    // kopecks, prices in hryvnia. 3 certificates at a 1,000.50 UAH sell price are worth 3,001.50 UAH, sent as 300,150.
    [Fact]
    public void Cabinet_amounts_are_kopecks_so_a_fund_is_worth_quantity_times_price_not_a_hundred_times_it()
    {
        var asset = new
        {
            id = 7,
            type = "fund",
            status = "active",
            prices = new { sellUAH = 1_000.50m },
            details = new { certificatesOwnedQuantity = 3m, totalAmountUAH = 300_150m, investedUAH = 285_000m },
        };

        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(asset),
            new { availableBalanceUAH = 20_000m, blockedBalanceUAH = 5_000m, totalBalanceUAH = 325_150m },
            InzhurFakes.Catalog(InzhurFakes.CatalogEntry(7, "Fund Seven", sellUah: 1_000.50m))));

        var fund = positions.Single(p => p.InstrumentType == InzhurHoldingsMapper.FundInstrumentType);
        fund.Symbol.Should().Be("Fund Seven");
        fund.NativeValue.Should().Be(3m * 1_000.50m);
        fund.UsdValue.Should().Be(CurrencyConverter.ToUsd(3_001.50m, "UAH"));
        fund.AverageCostUsd.Should().Be(CurrencyConverter.ToUsd(2_850m, "UAH") / 3m);
        var cash = positions.Single(p => p.Symbol == InzhurHoldingsMapper.CashSymbol);
        cash.Quantity.Should().Be(250m);
        // Holdings add up to the cabinet's own headline total once both are read in hryvnia.
        positions.Sum(p => p.NativeValue).Should().Be(325_150m / InzhurFakes.Kopecks);
    }

    [Fact]
    public void A_fund_without_a_name_on_its_row_takes_the_catalog_name_and_a_bond_its_isin()
    {
        var unnamedFund = new { id = 5, type = "fund", details = new { certificatesOwnedQuantity = 1m, totalAmountUAH = 1_000m } };
        var unnamedBond = new { id = 30, type = "bond", prices = new { sellUAH = 1_000m }, securityProperties = new { availableQuantity = 2m } };

        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(unnamedFund, unnamedBond),
            catalog: InzhurFakes.Catalog(
                InzhurFakes.CatalogEntry(5, "Fund Five"),
                new { assetId = 30, type = "bond", name = "Bond 30", isin = "UA4000000001" })));

        positions.Select(p => (p.Symbol, p.InstrumentType)).Should().BeEquivalentTo(
            [("Fund Five", "REIT"), ("UA4000000001", "BOND")]);
    }

    [Fact]
    public void An_asset_missing_from_the_catalog_keeps_the_id_fallback()
    {
        var unnamed = new { id = 99, type = "fund", details = new { certificatesOwnedQuantity = 1m, totalAmountUAH = 1_000m } };

        InzhurHoldingsMapper.Map(Portfolio(InzhurFakes.UserAssets(unnamed), catalog: InzhurFakes.Catalog()))
            .Should().ContainSingle().Which.Symbol.Should().Be("INZHUR-99");
    }

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
            details = new { certificatesOwnedQuantity = 10m, totalAmountUAH = 120_000m },
        };
        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(fund),
            new { availableBalanceUAH = 5_000m, blockedBalanceUAH = 0m, currency = "EUR" }));

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
            details = new { certificatesOwnedQuantity = 10m, totalAmountUAH = 120_000m },
        };
        var positions = InzhurHoldingsMapper.Map(Portfolio(
            InzhurFakes.UserAssets(fund),
            new { availableBalanceUAH = 5_000m, currency = 980 }));

        positions.Should().HaveCount(2);
        positions.Should().OnlyContain(p => p.Currency == "UAH");
        positions.Single(p => p.Symbol == "Fund F").UsdValue.Should().Be(CurrencyConverter.ToUsd(1_200m, "UAH"));
    }

    [Fact]
    public void Symbol_falls_back_from_name_to_title_to_the_catalog_to_isin_to_the_id()
    {
        static InzhurAsset Asset(string? name, string? title, string? isin, string type = "fund")
            => new(JsonSerializer.SerializeToElement(42), type, null, null, name, title, isin, null, null, null);
        var catalog = new InzhurCatalogAsset(JsonSerializer.SerializeToElement(42), "fund", "Catalog", null);

        InzhurHoldingsMapper.SymbolFor(Asset("Name", "Title", "ISIN")).Should().Be("Name");
        InzhurHoldingsMapper.SymbolFor(Asset(" ", "Title", "ISIN")).Should().Be("Title");
        InzhurHoldingsMapper.SymbolFor(Asset(null, null, "ISIN"), catalog).Should().Be("Catalog");
        InzhurHoldingsMapper.SymbolFor(Asset(null, null, "ISIN")).Should().Be("ISIN");
        InzhurHoldingsMapper.SymbolFor(Asset(null, null, null)).Should().Be("INZHUR-42");
        InzhurHoldingsMapper.SymbolFor(Asset(new string('x', 80), null, null)).Should().HaveLength(50);
    }

    [Fact]
    public void A_bond_is_named_by_its_isin_first_the_way_the_ledger_names_it()
        => InzhurHoldingsMapper.SymbolFor("bond", "War bond", "UA4000000001", "5").Should().Be("UA4000000001");
}

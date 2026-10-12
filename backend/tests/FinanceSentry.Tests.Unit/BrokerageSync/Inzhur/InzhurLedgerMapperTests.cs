using System.Text.Json;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

public class InzhurLedgerMapperTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<InzhurTransaction> Rows(params object[] rows)
        => JsonSerializer.Deserialize<List<InzhurTransaction>>(JsonSerializer.Serialize(rows), Web)!;

    [Theory]
    [InlineData("Deposit", 1)]
    [InlineData("Dividend crediting", 1)]
    [InlineData("Bonuses crediting", 1)]
    [InlineData("Conversion In", 1)]
    [InlineData("Withdrawal", -1)]
    [InlineData("Dividend payment", -1)]
    [InlineData("Bonus withdrawal", -1)]
    [InlineData("Dividend tax", -1)]
    [InlineData("Bonuses tax", -1)]
    [InlineData("Sale tax", -1)]
    [InlineData("Sale commission", -1)]
    [InlineData("Distribution tax", -1)]
    [InlineData("Conversion Out", -1)]
    [InlineData("Conversion Surcharge", -1)]
    public void Money_only_rows_carry_the_cabinets_sign_and_hryvnia(string type, int sign)
    {
        var ledger = InzhurLedgerMapper.Map(Rows(InzhurFakes.Transaction("x", type, 12.34m, "2026-01-02T03:04:05Z")));

        var cash = ledger.Cash.Should().ContainSingle().Subject;
        cash.Amount.Should().Be(sign * 12.34m);
        cash.Type.Should().Be(type);
        cash.DateTime.Should().Be(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        ledger.Trades.Should().BeEmpty();
    }

    [Fact]
    public void Ledger_amounts_on_the_wire_are_kopecks()
    {
        var ledger = InzhurLedgerMapper.Map(Rows(
            new { id = "p", type = "Purchase", amount = 98_765m, externalCreatedAt = "2026-01-02T00:00:00Z", asset = new { type = "fund", name = "Fund A" }, contract = new { quantity = 5m } },
            new { id = "t", type = "Sale tax", amount = 7m, externalCreatedAt = "2026-01-03T00:00:00Z" }));

        ledger.Trades.Should().ContainSingle().Which.Should().Match<InzhurLedgerTrade>(t => t.Proceeds == -987.65m && t.Price == 197.53m);
        ledger.Cash.Should().ContainSingle().Which.Amount.Should().Be(-0.07m);
    }

    [Theory]
    [InlineData("DIVIDEND")]
    [InlineData("COUPON")]
    [InlineData("REDEMPTION")]
    public void Distributions_keep_their_kind_and_direction(string kind)
    {
        var ledger = InzhurLedgerMapper.Map(Rows(
            InzhurFakes.Transaction("in", "Distribution crediting", 10m, "2026-01-02T00:00:00Z", "Fund A", distributionType: kind),
            InzhurFakes.Transaction("out", "Distribution payment", 10m, "2026-01-03T00:00:00Z", "Fund A", distributionType: kind)));

        ledger.Cash.Select(c => (c.Id, c.Amount, c.Code, c.Symbol)).Should().BeEquivalentTo(
            [("in", 10m, kind, "Fund A"), ("out", -10m, kind, "Fund A")]);
    }

    [Fact]
    public void Purchases_with_bonuses_count_as_purchases_and_bonds_are_named_by_isin()
    {
        var ledger = InzhurLedgerMapper.Map(Rows(
            InzhurFakes.Transaction("p", "Purchase for bonuses", 50m, "2026-01-02T00:00:00Z", "Fund A", quantity: 5m),
            InzhurFakes.Transaction("b", "Purchase", 2_000m, "2026-01-03T00:00:00Z", "War bond", quantity: 2m, assetType: "bond", isin: "UA4000000001")));

        ledger.Trades.Select(t => (t.Id, t.Symbol, t.Isin, t.Quantity, t.Price)).Should().BeEquivalentTo(
            [("p", "Fund A", (string?)null, 5m, 10m), ("b", "UA4000000001", "UA4000000001", 2m, 1_000m)]);
    }

    [Fact]
    public void A_purchase_without_a_contract_keeps_its_money_and_unknown_types_are_named_not_guessed()
    {
        var ledger = InzhurLedgerMapper.Map(Rows(
            InzhurFakes.Transaction("p", "Purchase", 100m, "2026-01-02T00:00:00Z", "Fund A"),
            InzhurFakes.Transaction("u", "Brand new type", 1m, "2026-01-03T00:00:00Z"),
            InzhurFakes.Transaction("d", "Deposit", 1m, "not a date")));

        ledger.Trades.Should().BeEmpty();
        ledger.Cash.Should().ContainSingle().Which.Should().Match<InzhurLedgerCash>(c => c.Id == "p" && c.Amount == -100m);
        ledger.SkippedTypes.Should().Equal("Brand new type", "Deposit");
    }

    // The regression the valuation bug left: the snapshot (kopecks) and the ledger (kopecks) of one fund must agree once
    // both are read in hryvnia - same units, and a cost basis the fills verify instead of an unexplained one.
    [Fact]
    public void Snapshot_and_ledger_reconcile_units_and_verify_the_cost_basis()
    {
        var userId = Guid.NewGuid();
        var portfolio = new InzhurPortfolio(
            JsonSerializer.Deserialize<InzhurUserAssetsResponse>(
                JsonSerializer.Serialize(InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 6m, 720m, 600m))), Web)!.Assets!,
            null);
        var ledger = InzhurLedgerMapper.Map(Rows(
            InzhurFakes.Transaction("s", "Sale", 440m, "2026-03-01T00:00:00Z", "Fund A", quantity: 4m),
            InzhurFakes.Transaction("p", "Purchase", 1_000m, "2026-02-01T00:00:00Z", "Fund A", quantity: 10m)));

        var position = InzhurHoldingsMapper.Map(portfolio).Should().ContainSingle().Subject;
        ledger.Trades.Where(t => t.Symbol == position.Symbol).Sum(t => t.Quantity).Should().Be(position.Quantity);

        var holding = new BrokerageHolding(
            userId, position.Symbol, position.InstrumentType, position.Quantity, position.UsdValue, InzhurHoldingsMapper.Provider,
            position.AverageCostUsd, currency: position.Currency, nativeValue: position.NativeValue);
        var trades = ledger.Trades.Select(t => new BrokerageTrade(
            userId, InzhurHoldingsMapper.Provider, t.Id, t.Id, null, t.Isin, t.Symbol, null, null, t.TradeDateTime, t.Quantity,
            t.Price, t.Proceeds, null, null, null, null, null, InzhurHoldingsMapper.DefaultCurrency, null)).ToList();

        new BrokerageCostBasisReconciler().Reconcile(holding, trades).State.Should().Be(BasisState.Verified);
    }

    [Fact]
    public void Fills_of_another_broker_never_explain_an_Inzhur_holding()
    {
        var userId = Guid.NewGuid();
        var holding = new BrokerageHolding(userId, "Fund A", "REIT", 1m, 1m, InzhurHoldingsMapper.Provider, averageCostUsd: 1m);
        var ibkrFill = new BrokerageTrade(
            userId, "ibkr", "e1", "t1", null, null, "Fund A", "O", null, DateTime.UtcNow, 1m, 1m, -1m, null, null, null, null, null, "USD", null);

        new BrokerageCostBasisReconciler().Reconcile(holding, [ibkrFill]).State.Should().Be(BasisState.Unknown);
    }
}

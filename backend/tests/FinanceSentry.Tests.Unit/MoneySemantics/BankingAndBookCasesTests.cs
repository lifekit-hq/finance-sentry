namespace FinanceSentry.Tests.Unit.MoneySemantics;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Xunit;

/// <summary>
/// Issue #1470 cases 7, 11, 13-16 and 18: banking aggregation and the book's cash / invested definition.
/// Expected values are written from the blessed table (docs/money-semantics.md sections 2, 3 and 8). Seed rates.
/// </summary>
public sealed class BankingAndBookCasesTests
{
    private static readonly BrokerageHoldingSummary[] NoBrokerage = [];
    private static readonly CryptoHoldingSummary[] NoCrypto = [];

    [Fact]
    public async Task Case_07_BankReader_1000Uah_And_100Eur_SumToUsd132_NeverTheNative1100()
    {
        using var _ = FxTableScope.Seed();
        var reader = ScenarioS.BankingReader(ScenarioS.Bank("checking", "UAH", 1_000m), ScenarioS.Bank("checking", "EUR", 100m));

        var summaries = await reader.GetAccountSummariesAsync(ScenarioS.UserId);
        var book = await ScenarioS.BookAsync(reader, NoBrokerage, NoCrypto);

        summaries.Sum(a => a.BalanceUsd ?? 0m).Should().Be(132.00m);
        book.BankingCashUsd.Should().Be(132.00m);
        book.TotalValueUsd.Should().Be(132.00m);
    }

    [Fact]
    public async Task Case_11_BankingAggregate_NetsTheCard_540_AndTheCardDisplaysPositive500()
    {
        using var _ = FxTableScope.Seed();
        var reader = ScenarioS.BankingReader(ScenarioS.Bank("checking", "EUR", 1_000m), ScenarioS.Bank("credit", "EUR", 500m));

        var summaries = await reader.GetAccountSummariesAsync(ScenarioS.UserId);
        var book = await ScenarioS.BookAsync(reader, NoBrokerage, NoCrypto);

        book.BankingCashUsd.Should().Be(540.00m);
        var card = summaries.Single(a => a.AccountType == "credit");
        card.CurrentBalance.Should().Be(500m, "the card's own display stays positive; only the aggregate nets it");
    }

    [Fact]
    public async Task Case_13_LiveNetWorthOnS_Is7864()
    {
        using var _ = FxTableScope.Seed();

        var book = await ScenarioS.BookOfSAsync();

        book.TotalValueUsd.Should().Be(7_864.00m);
    }

    [Fact]
    public async Task Case_14_CashUsdOnS_NetsTheCard_864_PlusBrokerCash300_Is1164()
    {
        using var _ = FxTableScope.Seed();

        var book = await ScenarioS.BookOfSAsync();

        book.BankingCashUsd.Should().Be(864.00m);
        book.CashUsd.Should().Be(1_164.00m);
    }

    [Fact]
    public async Task Case_15_InvestedValueUsdOnS_Is6700_UsdtIsACryptoPosition_NotCash()
    {
        using var _ = FxTableScope.Seed();

        var book = await ScenarioS.BookOfSAsync();

        book.InvestedValueUsd.Should().Be(6_700.00m);
        book.CashUsd.Should().Be(1_164.00m, "the 1,500 USDT is invested, so it is not in cash");
    }

    [Fact]
    public async Task Case_16_BrokerIdleCash300_IsInCash_AndAbsentFromInvested()
    {
        using var _ = FxTableScope.Seed();

        var book = await ScenarioS.BookOfSAsync();

        book.BrokerageCashUsd.Should().Be(300m);
        book.CashUsd.Should().Be(1_164.00m);
        book.InvestedValueUsd.Should().Be(6_700.00m);
    }

    [Fact]
    public async Task Case_18_BankingOnly_Checking100Eur_CardOwing500Eur_CashIsMinus432_NotClamped()
    {
        using var _ = FxTableScope.Seed();
        var reader = ScenarioS.BankingReader(ScenarioS.Bank("checking", "EUR", 100m), ScenarioS.Bank("credit", "EUR", 500m));

        var book = await ScenarioS.BookAsync(reader, NoBrokerage, NoCrypto);

        book.CashUsd.Should().Be(-432.00m);
        book.TotalValueUsd.Should().Be(-432.00m);
    }
}

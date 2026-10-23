namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;

public class CashLodgementClassifierTests
{
    [Theory]
    [InlineData("*ATMLDG CAPEL ST")]
    [InlineData("*ATMLDG EXCISE WAL")]
    [InlineData("  *atmldg Capel St")]
    public void Resolve_LodgementCredit_IsIncome(string description)
    {
        CashLodgementClassifier.Resolve(description, "credit").Should().Be(CategoryKeys.Income);
    }

    [Theory]
    [InlineData("*ATMLDG CAPEL ST", "debit")]
    [InlineData("*ATMLDG CAPEL ST", null)]
    [InlineData("*ATMWDL CAPEL ST", "credit")]
    [InlineData("ATM Withdrawal", "debit")]
    [InlineData("ATM Withdrawal", "credit")]
    [InlineData("Payment from Andrea Di Florio", "credit")]
    [InlineData("Cash lodgement Revolut", "credit")]
    [InlineData("", "credit")]
    [InlineData(null, "credit")]
    public void Resolve_NotALodgementCredit_ReturnsNull(string? description, string? transactionType)
    {
        CashLodgementClassifier.Resolve(description, transactionType).Should().BeNull();
    }
}

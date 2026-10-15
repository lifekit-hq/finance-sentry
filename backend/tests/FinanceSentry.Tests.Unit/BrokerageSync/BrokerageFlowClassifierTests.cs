namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FluentAssertions;
using Xunit;

public class BrokerageFlowClassifierTests
{
    [Theory]
    [InlineData("ibkr", "Deposits/Withdrawals", 1000, FlowClass.ExternalInflow)]
    [InlineData("ibkr", "Deposits & Withdrawals", -250, FlowClass.ExternalOutflow)]
    [InlineData("ibkr", "Dividends", 12, FlowClass.Income)]
    [InlineData("ibkr", "Dividends", -12, FlowClass.Fee)]
    [InlineData("ibkr", "Broker Interest Received", 3, FlowClass.Income)]
    [InlineData("ibkr", "Other Fees", -10, FlowClass.Fee)]
    [InlineData("ibkr", "Withholding Tax", -2, FlowClass.Tax)]
    [InlineData("ibkr", "Withholding Tax", 2, FlowClass.Income)]
    [InlineData("IBKR", "dividends", 5, FlowClass.Income)]
    [InlineData("inzhur", "Deposit", 2200, FlowClass.ExternalInflow)]
    [InlineData("inzhur", "Withdrawal", -500, FlowClass.ExternalOutflow)]
    [InlineData("inzhur", "Dividend crediting", 40, FlowClass.Income)]
    [InlineData("inzhur", "Dividend payment", -40, FlowClass.ExternalOutflow)]
    [InlineData("inzhur", "Dividend tax", -9, FlowClass.Tax)]
    [InlineData("inzhur", "Sale commission", -3, FlowClass.Fee)]
    public void Classify_MapsAKnownTypeToItsFlowClass(string provider, string type, int amount, FlowClass expected)
        => BrokerageFlowClassifier.Classify(provider, type, amount).Should().Be(expected);

    [Theory]
    [InlineData("ibkr", "Trade", 100)]
    [InlineData("ibkr", "Something New", 100)]
    [InlineData("inzhur", "Purchase", -100)]
    [InlineData("binance", "Deposit", 100)]
    [InlineData("ibkr", "Deposits/Withdrawals", 0)]
    public void Classify_LeavesAnUnknownTypeOrAZeroAmountOut(string provider, string type, int amount)
        => BrokerageFlowClassifier.Classify(provider, type, amount).Should().BeNull();

    [Theory]
    [InlineData(FlowClass.ExternalInflow, true)]
    [InlineData(FlowClass.ExternalOutflow, true)]
    [InlineData(FlowClass.Income, false)]
    [InlineData(null, false)]
    public void IsExternal_IsTrueForDepositsAndWithdrawalsOnly(FlowClass? flowClass, bool expected)
        => BrokerageFlowClassifier.IsExternal(flowClass).Should().Be(expected);
}

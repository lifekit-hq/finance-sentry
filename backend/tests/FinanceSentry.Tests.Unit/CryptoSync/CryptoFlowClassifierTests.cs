using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class CryptoFlowClassifierTests
{
    private static CryptoFlowEvent Flow(
        string provider, string type, decimal amount, string unit, string? counterparty, decimal? fee = null, bool isFiat = true) =>
        CryptoFlowEvent.Create(Guid.NewGuid(), provider,
            new CryptoFlow("e-1", type, DateTime.UtcNow, amount, unit, isFiat, isFiat ? null : 60000m, fee, fee is null ? null : unit,
                counterparty, "completed"),
            DateTime.UtcNow);

    [Fact]
    public void Fixture19_TheRevolutReceiveAndTheCardBuy_EnterTheSleeve_AndTheBuysFeeIsAFee()
    {
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, 500m, "EUR", "revolut"))
            .Should().Equal(new CryptoFlowLeg(FlowClass.ExternalInflow, 500m, "EUR"));
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.Binance, CryptoFlowTypes.CardBuy, 100m, "EUR", "Credit Card", fee: 2m))
            .Should().Equal(new CryptoFlowLeg(FlowClass.ExternalInflow, 100m, "EUR"), new CryptoFlowLeg(FlowClass.Fee, 2m, "EUR"));
    }

    [Fact]
    public void Fixture19CashBalanceVariant_ABuyFromTheVenuesOwnBalance_LeavesOnlyItsFee()
    {
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.Binance, CryptoFlowTypes.CardBuy, 100m, "EUR", "Cash Balance", fee: 2m))
            .Should().Equal(new CryptoFlowLeg(FlowClass.Fee, 2m, "EUR"));
    }

    [Theory]
    [InlineData(CryptoFlowTypes.FiatDeposit, FlowClass.ExternalInflow)]
    [InlineData(CryptoFlowTypes.FiatWithdrawal, FlowClass.ExternalOutflow)]
    [InlineData(CryptoFlowTypes.CardSell, FlowClass.ExternalOutflow)]
    [InlineData(CryptoFlowTypes.Send, FlowClass.ExternalOutflow)]
    public void EachMovement_CrossesTheSleevesEdge(string type, FlowClass expected)
    {
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.Binance, type, 10m, "EUR", null))
            .Should().Equal(new CryptoFlowLeg(expected, 10m, "EUR"));
    }

    [Fact]
    public void AnUnknownType_ContributesNothing()
    {
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.Binance, "mystery", 10m, "EUR", null)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(CryptoFlowTypes.Reward)]
    [InlineData(CryptoFlowTypes.Airdrop)]
    public void ARewardOrAirdrop_IsIncome_NeverMoneyPutIn(string type)
    {
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.Binance, type, 30m, "USDT", null, isFiat: false))
            .Should().Equal(new CryptoFlowLeg(FlowClass.Income, 30m, "USDT"));
    }

    [Fact]
    public void ADustConversion_MovesNothing_ButItsServiceCharge()
    {
        CryptoFlowClassifier.AtSleeve(Flow(CryptoExchangeProvider.Binance, CryptoFlowTypes.DustConversion, 0.2m, "BNB", null, fee: 0.0001m, isFiat: false))
            .Should().Equal(new CryptoFlowLeg(FlowClass.Fee, 0.0001m, "BNB"));
    }
}

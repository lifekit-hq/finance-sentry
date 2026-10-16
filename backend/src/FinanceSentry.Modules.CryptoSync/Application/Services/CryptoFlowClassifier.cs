using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.CryptoSync.Domain;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>One classified part of a venue movement, in the movement's own unit.</summary>
public readonly record struct CryptoFlowLeg(FlowClass Class, decimal Amount, string Unit);

/// <summary>
/// Classifies a recorded <see cref="CryptoFlowEvent"/> at read time, by scope (finance-framework §4.2 rule 1): the
/// adapter stores what the venue said, this decides what it means for a figure.
/// </summary>
public static class CryptoFlowClassifier
{
    /// <summary>Binance's <c>paymentMethod</c> for a buy paid from the venue's own fiat balance.</summary>
    public const string CashBalanceMethod = "Cash Balance";

    /// <summary>Revolut X's account type for the owner's own Revolut app.</summary>
    public const string RevolutAccount = "revolut";

    private const string ExternalAccountPrefix = "external_";

    /// <summary>
    /// The movement's legs for the crypto sleeve, where every venue is in scope and anything outside the venues is
    /// not: fiat in, a card buy and a transfer in are external inflows, fiat out, a sell for fiat and a transfer out
    /// are external outflows, and the fee the venue kept is a fee. A buy paid from the venue's own fiat balance moves
    /// nothing across the sleeve's edge, so only its fee remains.
    /// </summary>
    public static IReadOnlyList<CryptoFlowLeg> AtSleeve(CryptoFlowEvent flow)
    {
        FlowClass? movement = flow.ProviderType switch
        {
            CryptoFlowTypes.FiatDeposit or CryptoFlowTypes.Receive => FlowClass.ExternalInflow,
            CryptoFlowTypes.CardBuy when string.Equals(
                flow.CounterpartyRef, CashBalanceMethod, StringComparison.OrdinalIgnoreCase) => null,
            CryptoFlowTypes.CardBuy => FlowClass.ExternalInflow,
            // shortcut: where a sell's fiat proceeds land is undocumented; read as paid out until a real row shows
            // they stay on the venue (then it is an internal trade, like a Cash Balance buy).
            CryptoFlowTypes.FiatWithdrawal or CryptoFlowTypes.CardSell or CryptoFlowTypes.Send => FlowClass.ExternalOutflow,
            _ => null,
        };

        var legs = new List<CryptoFlowLeg>(2);
        if (movement is { } cls)
        {
            legs.Add(new CryptoFlowLeg(cls, flow.Amount, flow.Unit));
        }

        if (flow.Fee is > 0m && !string.IsNullOrEmpty(flow.FeeUnit))
        {
            legs.Add(new CryptoFlowLeg(FlowClass.Fee, flow.Fee.Value, flow.FeeUnit));
        }

        return legs;
    }

    /// <summary>
    /// Whether the movement's other side is one of the owner's own accounts outside the venues, so a scope that also
    /// holds that account reads it as internal. True: a Revolut X move from or to the owner's Revolut app. False: an
    /// external bank account or wallet. Null when the venue does not say: Binance's fiat legs name only a method, so a
    /// match against a linked bank leg has to decide.
    /// </summary>
    public static bool? IsOwnersOwnAccount(CryptoFlowEvent flow)
    {
        if (flow.Provider != CryptoExchangeProvider.RevolutX || string.IsNullOrEmpty(flow.CounterpartyRef))
        {
            return null;
        }

        if (flow.CounterpartyRef == RevolutAccount)
        {
            return true;
        }

        return flow.CounterpartyRef.StartsWith(ExternalAccountPrefix, StringComparison.Ordinal) ? false : null;
    }
}

using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.CryptoSync.Domain;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>One classified part of a venue movement, in the movement's own unit.</summary>
public readonly record struct CryptoFlowLeg(FlowClass Class, decimal Amount, string Unit);

/// <summary>Which way a transfer leg crossed its venue's edge.</summary>
public enum CryptoTransferDirection
{
    In,
    Out,
}

/// <summary>
/// Classifies a recorded <see cref="CryptoFlowEvent"/> at read time, by scope (finance-framework §4.2 rule 1): the
/// adapter stores what the venue said, this decides what it means for a figure.
/// </summary>
public static class CryptoFlowClassifier
{
    /// <summary>Binance's <c>paymentMethod</c> for a buy paid from the venue's own fiat balance.</summary>
    public const string CashBalanceMethod = "Cash Balance";

    /// <summary>The Revolut X account type of a wallet outside Revolut: another venue, or anyone's wallet.</summary>
    public const string ExternalCryptoWallet = "external_crypto_wallet";

    /// <summary>The spelling Revolut X's API reports for the same account type (seen on a live SOL send).</summary>
    public const string ExternalCrypto = "external_crypto";

    /// <summary>True for either spelling of the Revolut X external-wallet account type.</summary>
    public static bool IsExternalWalletType(string? counterpartyRef) =>
        string.Equals(counterpartyRef, ExternalCryptoWallet, StringComparison.OrdinalIgnoreCase)
        || string.Equals(counterpartyRef, ExternalCrypto, StringComparison.OrdinalIgnoreCase);

    /// <summary>The counterparty a Binance chain leg records for a transfer with another Binance account.</summary>
    public const string BinanceInternalTransfer = "binance_internal";

    /// <summary>
    /// The direction of a crypto transfer over a chain - a leg another venue in the sleeve may hold the other side of
    /// (S2b): a Binance on-chain deposit or withdrawal, a Revolut X receive or send with an external crypto wallet.
    /// Null for anything else (fiat, a transfer with another Binance account or the owner's Revolut app).
    /// </summary>
    public static CryptoTransferDirection? ChainTransfer(CryptoFlowEvent flow)
    {
        if (flow.IsFiat)
        {
            return null;
        }

        return flow.ProviderType switch
        {
            CryptoFlowTypes.CryptoDeposit when !IsBinanceInternal(flow) => CryptoTransferDirection.In,
            CryptoFlowTypes.CryptoWithdrawal when !IsBinanceInternal(flow) => CryptoTransferDirection.Out,
            CryptoFlowTypes.Receive when IsExternalWallet(flow) => CryptoTransferDirection.In,
            CryptoFlowTypes.Send when IsExternalWallet(flow) => CryptoTransferDirection.Out,
            _ => null,
        };
    }

    /// <summary>What a sent leg took out of its venue in its own unit: the amount plus a fee kept in that unit.</summary>
    public static decimal SentGross(CryptoFlowEvent flow) => flow.Amount + SameUnitFee(flow);

    /// <summary>The fee a leg reports in its own unit; zero when none is, or it is in another unit.</summary>
    public static decimal SameUnitFee(CryptoFlowEvent flow) =>
        flow.Fee is > 0m and var fee && string.Equals(flow.FeeUnit, flow.Unit, StringComparison.OrdinalIgnoreCase) ? fee : 0m;

    /// <summary>
    /// The movement's legs for the crypto sleeve, where every venue is in scope and anything outside the venues is
    /// not: fiat in, a card buy and a transfer or coin deposit in are external inflows, fiat out, a sell for fiat and
    /// a transfer or coin withdrawal out are external outflows, a reward or airdrop is income, and the fee the venue kept is a fee. A buy paid from
    /// the venue's own fiat balance moves nothing across the sleeve's edge, so only its fee remains. A transfer the
    /// other venue's leg matches is not read here: <see cref="CryptoTransferMatcher"/> makes it internal.
    /// </summary>
    public static IReadOnlyList<CryptoFlowLeg> AtSleeve(CryptoFlowEvent flow)
    {
        FlowClass? movement = flow.ProviderType switch
        {
            CryptoFlowTypes.FiatDeposit or CryptoFlowTypes.Receive or CryptoFlowTypes.CryptoDeposit => FlowClass.ExternalInflow,
            // Income is part of the return, never money put in; a dust swap moves nothing but its fee (below).
            CryptoFlowTypes.Reward or CryptoFlowTypes.Airdrop => FlowClass.Income,
            CryptoFlowTypes.CardBuy when string.Equals(
                flow.CounterpartyRef, CashBalanceMethod, StringComparison.OrdinalIgnoreCase) => null,
            CryptoFlowTypes.CardBuy => FlowClass.ExternalInflow,
            // shortcut: where a sell's fiat proceeds land is undocumented; read as paid out until a real row shows
            // they stay on the venue (then it is an internal trade, like a Cash Balance buy).
            CryptoFlowTypes.FiatWithdrawal or CryptoFlowTypes.CardSell or CryptoFlowTypes.Send
                or CryptoFlowTypes.CryptoWithdrawal => FlowClass.ExternalOutflow,
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

    private static bool IsBinanceInternal(CryptoFlowEvent flow) =>
        string.Equals(flow.CounterpartyRef, BinanceInternalTransfer, StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalWallet(CryptoFlowEvent flow) => IsExternalWalletType(flow.CounterpartyRef);
}

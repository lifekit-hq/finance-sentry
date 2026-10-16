using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// One money movement across a venue's edge as the venue reported it (S2a, S2b): a fiat deposit or withdrawal, a card
/// buy or sell, a transfer in or out, a coin deposit or withdrawal. Raw, never classified: the class depends on the
/// scope a figure measures, so the reader classifies it (finance-framework §4.2 rule 1). Insert-only and idempotent on
/// <c>(UserId, Provider, ProviderType, EventId)</c>, like <see cref="CryptoTradeRecord"/>.
/// </summary>
public sealed class CryptoFlowEvent : IUserOwned
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string ProviderType { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; }
    public decimal Amount { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public bool IsFiat { get; private set; }
    public decimal? UnitPriceUsd { get; private set; }
    public decimal? Fee { get; private set; }
    public string? FeeUnit { get; private set; }
    public string? CounterpartyRef { get; private set; }
    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// The chain transaction id (hash) as the venue reported it, kept raw: matching normalizes it (S2b). Null where
    /// the venue gives none (a fiat leg, a Revolut X transfer read before S2b).
    /// </summary>
    public string? TxRef { get; private set; }

    /// <summary>The chain the transfer used, in the venue's own vocabulary (Binance <c>SOL</c>, Revolut X <c>SOLANA</c>).</summary>
    public string? Network { get; private set; }

    /// <summary>When the row was read; also when <see cref="UnitPriceUsd"/> was taken.</summary>
    public DateTime RecordedAt { get; private set; }

    private CryptoFlowEvent() { }

    public static CryptoFlowEvent Create(Guid userId, string provider, CryptoFlow flow, DateTime recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(flow);

        return new CryptoFlowEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            EventId = flow.EventId,
            ProviderType = flow.ProviderType,
            OccurredAt = flow.OccurredAt,
            Amount = flow.Amount,
            Unit = flow.Unit,
            IsFiat = flow.IsFiat,
            UnitPriceUsd = flow.UnitPriceUsd,
            Fee = flow.Fee,
            FeeUnit = flow.FeeUnit,
            CounterpartyRef = flow.CounterpartyRef,
            Status = flow.Status,
            TxRef = flow.TxRef,
            Network = flow.Network,
            RecordedAt = recordedAt,
        };
    }
}

/// <summary>The <see cref="CryptoFlowEvent.ProviderType"/> values: what the venue says the movement was.</summary>
public static class CryptoFlowTypes
{
    /// <summary>Binance <c>fiat/orders</c> deposit: fiat in from a bank or card.</summary>
    public const string FiatDeposit = "fiat_deposit";

    /// <summary>Binance <c>fiat/orders</c> withdrawal: fiat out to a bank or card.</summary>
    public const string FiatWithdrawal = "fiat_withdrawal";

    /// <summary>Binance <c>fiat/payments</c> buy: crypto bought with fiat paid by the method in the counterparty.</summary>
    public const string CardBuy = "card_buy";

    /// <summary>Binance <c>fiat/payments</c> sell: crypto sold for fiat paid out.</summary>
    public const string CardSell = "card_sell";

    /// <summary>Revolut X <c>receive</c>: an asset in from the account type in the counterparty.</summary>
    public const string Receive = "receive";

    /// <summary>Revolut X <c>send</c>: an asset out to the account type in the counterparty.</summary>
    public const string Send = "send";

    /// <summary>Binance <c>capital/deposit/hisrec</c>: a coin in over a chain, or from another Binance account.</summary>
    public const string CryptoDeposit = "crypto_deposit";

    /// <summary>Binance <c>capital/withdraw/history</c>: a coin out over a chain, or to another Binance account.</summary>
    public const string CryptoWithdrawal = "crypto_withdrawal";

    /// <summary>
    /// Earn interest or a staking reward (Binance Simple Earn flexible and locked rewards, Revolut X <c>reward</c>):
    /// the asset received, income within growth and never money put in.
    /// </summary>
    public const string Reward = "reward";

    /// <summary>Binance <c>assetDividend</c>: a distribution or airdrop of the asset received, income like a <see cref="Reward"/>.</summary>
    public const string Airdrop = "airdrop";

    /// <summary>
    /// Binance dust conversion (<c>asset/dribblet</c>): small balances swapped for BNB. The swap is internal, so
    /// only <see cref="CryptoFlowEvent.Fee"/> (the service charge, in BNB) counts; <see cref="CryptoFlowEvent.Amount"/> is the BNB received.
    /// </summary>
    public const string DustConversion = "dust_conversion";
}

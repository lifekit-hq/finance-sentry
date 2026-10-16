using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// One money movement across a venue's edge as the venue reported it (S2a): a fiat deposit or withdrawal, a card buy
/// or sell, a transfer in or out. Raw, never classified: the class depends on the scope a figure measures, so the
/// reader classifies it (finance-framework §4.2 rule 1). Insert-only and idempotent on
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
}

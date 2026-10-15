namespace FinanceSentry.Modules.Wealth.Domain;

using FinanceSentry.Core.Auth;

public sealed class NetWorthSnapshot : IUserOwned
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateOnly SnapshotDate { get; init; }
    public decimal BankingTotal { get; init; }
    public decimal BrokerageTotal { get; init; }
    public decimal CryptoTotal { get; init; }
    public decimal TotalNetWorth { get; init; }
    public string Currency { get; init; } = "USD";
    public DateTimeOffset TakenAt { get; init; }

    /// <summary>
    /// Comma-separated sleeve names whose value was carried forward from a prior
    /// snapshot because the live feed was stale, missing, or a failed sync.
    /// Empty/null = every sleeve was measured fresh this day. Lets consumers
    /// distinguish a measured net worth from a partially estimated one instead of
    /// treating carried-forward or $0 sync-failure days as real movement.
    /// </summary>
    public string? StaleSleeves { get; init; }

    /// <summary>
    /// True for a row reconstructed by the pre-history backfill (banking sleeve only,
    /// walked backward from transaction deltas) rather than measured by the daily
    /// snapshot job. Approximate rows use today's FX rate table, not the rate on the
    /// snapshot date, and carry BrokerageTotal = CryptoTotal = 0 because those sleeves
    /// cannot be reconstructed from transaction history.
    /// </summary>
    public bool IsApproximate { get; init; }

    /// <summary>
    /// Cash apart from invested assets, USD: bank balances (signed, card debt netted in), idle broker cash and
    /// fiat on crypto venues - the <see cref="FinanceSentry.Core.Interfaces.BookFigures.CashUsd"/> definition.
    /// Always <see cref="TotalNetWorth"/> - <see cref="BrokerageInvested"/> - <see cref="CryptoInvested"/>.
    /// Null = no split for this day (a row from before the split existed, or one the split cannot be
    /// measured for) - never zero.
    /// </summary>
    public decimal? CashTotal { get; init; }

    /// <summary>Brokerage sleeve without its idle cash (the invested positions); null = no split, as <see cref="CashTotal"/>.</summary>
    public decimal? BrokerageInvested { get; init; }

    /// <summary>Crypto sleeve without venue fiat (the invested positions); null = no split, as <see cref="CashTotal"/>.</summary>
    public decimal? CryptoInvested { get; init; }

    /// <summary>
    /// The worst currency-conversion status among the figures the snapshot job summed this row from (see
    /// <see cref="FinanceSentry.Core.Utils.FxStatus"/>); a carried-forward sleeve folds in the previous row's
    /// status and a backfilled row stores the worst of its accounts'. Null = not recorded (a row from before the
    /// status was kept); history then falls back to <see cref="IsApproximate"/>.
    /// </summary>
    public FinanceSentry.Core.Utils.FxStatus? FxStatus { get; init; }
}

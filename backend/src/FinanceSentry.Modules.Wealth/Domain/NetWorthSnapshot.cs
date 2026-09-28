namespace FinanceSentry.Modules.Wealth.Domain;

public sealed class NetWorthSnapshot
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
}

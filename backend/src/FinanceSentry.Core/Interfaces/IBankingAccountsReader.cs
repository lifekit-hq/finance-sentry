namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

public interface IBankingAccountsReader
{
    /// <summary>
    /// The accounts whose balance counts in live totals. A retired account, and one that has been absent from its
    /// provider's list for 24 h or more, is left out (docs/money-semantics.md, "Closed and vanished accounts"), so a sum
    /// over these never counts a balance that moved to a replacement card.
    /// </summary>
    Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The accounts <see cref="GetAccountSummariesAsync"/> leaves out (retired or vanished), for readers that label
    /// history with an account's currency and provider. Never sum their balances. Empty unless the reader knows them.
    /// </summary>
    Task<IReadOnlyList<BankingAccountSummary>> GetNonLiveAccountSummariesAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BankingAccountSummary>>([]);

    /// <summary>
    /// Returns the distinct set of user IDs that have at least one active bank account.
    /// Used by background jobs that fan out per user (e.g. the liquidity sentinel).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns lightweight account snapshots for projection (balance + currency only).
    /// Avoids loading sync-job history that <see cref="GetAccountSummariesAsync"/> pulls.
    /// </summary>
    Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default);
}

/// <param name="LinkedAtUtc">When the account was linked, so a reader over past days can leave it out before then; null when unknown.</param>
public record AccountBalanceSnapshot(
    Guid AccountId,
    string BankName,
    string AccountType,
    string AccountNumberLast4,
    string Currency,
    decimal? CurrentBalance,
    DateTime? LinkedAtUtc = null)
{
    public string DisplayName => $"{BankName} ···{AccountNumberLast4}";
}

public record BankingAccountSummary(
    Guid AccountId,
    string BankName,
    string AccountType,
    string AccountNumberLast4,
    string Provider,
    string Currency,
    decimal? CurrentBalance,
    decimal? BalanceUsd,
    string SyncStatus,
    DateTime? LastSyncTimestamp,
    DateTime? LastSuccessfulSyncTimestamp = null,
    Guid? MonobankCredentialId = null,
    Guid? TrueLayerConnectionId = null,
    string? ProductType = null,
    FxStatus FxStatus = FxStatus.Exact);

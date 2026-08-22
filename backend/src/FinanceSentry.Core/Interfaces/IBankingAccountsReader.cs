namespace FinanceSentry.Core.Interfaces;

public interface IBankingAccountsReader
{
    Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default);

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

public record AccountBalanceSnapshot(
    Guid AccountId,
    string BankName,
    string AccountNumberLast4,
    string Currency,
    decimal? CurrentBalance)
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
    string? ProductType = null);

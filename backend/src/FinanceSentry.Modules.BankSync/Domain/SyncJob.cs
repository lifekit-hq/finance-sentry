namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;

/// <summary>
/// Domain entity representing a sync job attempt.
/// Tracks sync operation history and status for monitoring.
/// </summary>
public class SyncJob : Entity, IUserOwned
{
    /// <summary>
    /// Foreign key to BankAccount.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// User who owns this sync job (denormalised from BankAccount for query performance).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Correlation ID for distributed tracing (nullable — not all jobs have one).
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Current status: pending, running, success, failed.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// When the sync job started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the sync job completed (null if still running).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Provider error code (e.g., ITEM_LOGIN_REQUIRED, MONOBANK_RATE_LIMITED).
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// How many transactions were successfully synced (legacy field — kept for compatibility).
    /// </summary>
    public int TransactionsSynced { get; set; }

    /// <summary>
    /// Total transactions fetched from the provider during this job.
    /// </summary>
    public int TransactionCountFetched { get; set; }

    /// <summary>
    /// New (non-duplicate) transactions saved to the database after deduplication.
    /// </summary>
    public int TransactionCountDeduped { get; set; }

    /// <summary>
    /// Date of the last transaction synced during this job.
    /// </summary>
    public DateTime? LastTransactionDate { get; set; }

    /// <summary>
    /// Number of retry attempts for this job.
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Navigation property to parent account.
    /// </summary>
    public BankAccount? Account { get; set; }

    /// <summary>
    /// Constructor for EF Core.
    /// </summary>
    public SyncJob()
    {
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Constructor for creating new sync job.
    /// </summary>
    public SyncJob(Guid accountId, Guid userId) : this()
    {
        AccountId = accountId;
        UserId = userId;
    }

    /// <summary>
    /// Mark sync job as completed successfully.
    /// </summary>
    public void MarkSuccess(int transactionCountFetched, int transactionCountDeduped, DateTime? lastTransactionDate = null)
    {
        Status = "success";
        CompletedAt = DateTime.UtcNow;
        TransactionCountFetched = transactionCountFetched;
        TransactionCountDeduped = transactionCountDeduped;
        TransactionsSynced = transactionCountDeduped; // keep legacy field in sync
        LastTransactionDate = lastTransactionDate;
        ErrorMessage = null;
        ErrorCode = null;
    }

    /// <summary>
    /// Mark sync job as failed.
    /// </summary>
    public void MarkFailed(string errorMessage, string? errorCode = null)
    {
        Status = "failed";
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Mark sync job as running.
    /// </summary>
    public void MarkRunning()
    {
        Status = "running";
    }

    /// <summary>
    /// Calculate duration of sync job.
    /// </summary>
    public TimeSpan GetDuration()
    {
        var end = CompletedAt ?? DateTime.UtcNow;
        return end - StartedAt;
    }
}

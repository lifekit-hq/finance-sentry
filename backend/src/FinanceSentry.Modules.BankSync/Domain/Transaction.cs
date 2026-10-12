namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;

/// <summary>
/// Domain entity representing an individual transaction.
/// Immutable after creation. Includes deduplication via unique_hash.
/// </summary>
public class Transaction : Entity, IUserOwned
{
    /// <summary>
    /// Foreign key to BankAccount.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Transaction amount (always positive for clarity; direction indicated by type).
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Date the transaction was actually posted to the account.
    /// Nullable for pending transactions (not yet posted).
    /// </summary>
    public DateTime? PostedDate { get; set; }

    /// <summary>
    /// Date the transaction occurred (authorization date).
    /// </summary>
    public DateTime TransactionDate { get; set; }

    /// <summary>
    /// Description or merchant information.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 hash for deduplication.
    /// Hash of: account_id|amount|date|description
    /// Unique constraint at database level per account.
    /// </summary>
    public string UniqueHash { get; set; } = string.Empty;

    /// <summary>
    /// Whether transaction is still pending (not officially posted).
    /// Treat pending + posted as different transactions (different dates).
    /// </summary>
    public bool IsPending { get; set; }

    /// <summary>
    /// Optional transaction type (debit, credit, transfer, etc.).
    /// </summary>
    public string? TransactionType { get; set; }

    /// <summary>
    /// Optional merchant or counterparty name.
    /// </summary>
    public string? MerchantName { get; set; }

    /// <summary>
    /// Resolved canonical category key (a <see cref="Category.Key"/>, e.g. "FOOD_AND_DRINK").
    /// Derived at ingestion from the raw provider signal via the category resolver.
    /// </summary>
    public string? MerchantCategory { get; set; }

    /// <summary>
    /// Raw ISO 18245 Merchant Category Code from the provider (Monobank), if any.
    /// Retained so the category can be re-resolved when the MCC map changes.
    /// </summary>
    public int? Mcc { get; set; }

    /// <summary>
    /// Raw provider-native category value (e.g. a PFC-style canonical key or TrueLayer classification), if any.
    /// Retained so the category can be re-resolved without re-syncing the provider.
    /// </summary>
    public string? SourceCategory { get; set; }

    /// <summary>
    /// Denormalized user FK for efficient user-scoped queries without joining BankAccount.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Soft-delete flag. Set to false by DELETE /accounts/{id} to preserve audit trail.
    /// All user-facing queries MUST filter WHERE IsActive = true.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when this transaction was soft-deleted. Null if not deleted.
    /// Set once on soft-delete; never cleared.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Reason for archival. Set to 'retention_policy_24m' by DataRetentionJob (T527).
    /// Null for user-initiated soft-deletes.
    /// </summary>
    public string? ArchivedReason { get; set; }

    /// <summary>
    /// Navigation property to parent account.
    /// </summary>
    public BankAccount? Account { get; set; }

    /// <summary>
    /// Constructor for EF Core.
    /// </summary>
    public Transaction()
    {
    }

    /// <summary>
    /// Constructor for creating new transaction.
    /// </summary>
    public Transaction(Guid accountId, Guid userId, decimal amount, DateTime transactionDate,
        string description, string uniqueHash, bool isPending = false)
    {
        AccountId = accountId;
        UserId = userId;
        Amount = amount;
        TransactionDate = transactionDate;
        Description = description;
        UniqueHash = uniqueHash;
        IsPending = isPending;
    }

    /// <summary>
    /// Soft-deletes this transaction. Financial data fields remain unchanged (immutable).
    /// </summary>
    public void SoftDelete()
    {
        IsActive = false;
        DeletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Validates invariants required for transaction integrity.
    /// </summary>
    public void ValidateInvariants()
    {
        if (Amount < 0)
            throw new ArgumentException("Amount cannot be negative");
        if (string.IsNullOrWhiteSpace(Description))
            throw new ArgumentException("Description cannot be empty");
        if (string.IsNullOrWhiteSpace(UniqueHash))
            throw new ArgumentException("UniqueHash cannot be empty");
    }
}

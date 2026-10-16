namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Domain;

public class BankAccount : Entity, IHasUpdatedAt, IUserOwned
{
    /// <summary>
    /// <see cref="Provider"/> of an account the startup smoke seed writes: fake data with no provider behind it,
    /// so every cross-user read (<c>GetAllActiveUnscopedAsync</c>: the sync scheduler, background jobs) skips it.
    /// </summary>
    public const string SeededProvider = "seeded";

    public Guid UserId { get; set; }

    public string ExternalAccountId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public Guid? MonobankCredentialId { get; set; }

    public Guid? TrueLayerConnectionId { get; set; }

    public string BankName { get; set; } = string.Empty;

    public string AccountType { get; set; } = string.Empty;

    /// <summary>
    /// Provider-specific product/card type, preserved verbatim (e.g. Monobank "black" / "white" /
    /// "platinum" / "fop"). Lets currency sub-accounts of the same physical card be grouped and
    /// labelled. Null for providers that don't expose a card product. <see cref="AccountType"/>
    /// stays the generic normalized type.
    /// </summary>
    public string? ProductType { get; set; }

    public string AccountNumberLast4 { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    public string Currency { get; set; } = "EUR";

    /// <summary>
    /// For credit accounts, the outstanding amount owed (positive = debt), per the
    /// <see cref="FinanceSentry.Core.Utils.AccountBalanceMath"/> liability convention —
    /// aggregations negate it. For all other accounts, the user's own funds.
    /// </summary>
    public decimal? CurrentBalance { get; set; }

    /// <summary>
    /// The account's credit limit, when the provider exposes one (Monobank client-info
    /// creditLimit, TrueLayer card balance credit_limit). Null for non-credit accounts.
    /// </summary>
    public decimal? CreditLimit { get; set; }

    public string SyncStatus { get; set; } = "pending";

    public string? LastSyncError { get; set; }

    /// <summary>
    /// Per-account transaction-sync watermark. Null means the account has never completed a
    /// transaction import and the next sync must run the provider's initial-history window.
    /// Deliberately per-account: a credential/connection-level timestamp lets the first-synced
    /// account starve its siblings' fetch windows (the "white card never syncs" bug).
    /// </summary>
    public DateTime? LastTransactionSyncAt { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The provider-reported IBAN (Monobank client-info, TrueLayer account number). Never logged.</summary>
    public string? Iban { get; set; }

    /// <summary>Every masked PAN the provider lists for the account (Monobank lists several on premium cards). Never logged in full.</summary>
    public string[]? MaskedPans { get; set; }

    /// <summary>A TrueLayer card's optional <c>valid_to</c> as the provider sends it (year-month, e.g. "2027-01").</summary>
    public string? CardValidTo { get; set; }

    // Closure and reissue bookkeeping: stored and read by later increments, written by nothing yet.
    public DateTimeOffset? ClosedAt { get; set; }

    public string? ClosedReason { get; set; }

    public string? ClosedBy { get; set; }

    public Guid? PredecessorAccountId { get; set; }

    public Guid? CreatedBy { get; set; }

    public Guid? UpdatedBy { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<SyncJob> SyncJobs { get; set; } = [];
    public MonobankCredential? MonobankCredential { get; set; }
    public TrueLayerConnection? TrueLayerConnection { get; set; }

    public BankAccount() { }

    public BankAccount(Guid userId, string externalAccountId, string bankName, string accountType,
        string accountNumberLast4, string ownerName, string currency, Guid createdBy,
        string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider cannot be empty.", nameof(provider));
        if (string.IsNullOrWhiteSpace(externalAccountId))
            throw new ArgumentException("ExternalAccountId cannot be empty.", nameof(externalAccountId));
        if (string.IsNullOrWhiteSpace(bankName))
            throw new ArgumentException("BankName cannot be empty.", nameof(bankName));
        if (string.IsNullOrWhiteSpace(accountType))
            throw new ArgumentException("AccountType cannot be empty.", nameof(accountType));
        if (accountNumberLast4.Length != 4 || !accountNumberLast4.All(char.IsDigit))
            throw new ArgumentException("AccountNumberLast4 must be exactly 4 digits.", nameof(accountNumberLast4));

        UserId = userId;
        ExternalAccountId = externalAccountId;
        Provider = provider;
        BankName = bankName;
        AccountType = accountType;
        AccountNumberLast4 = accountNumberLast4;
        OwnerName = ownerName;
        Currency = currency.ToUpperInvariant();
        CreatedBy = createdBy;
        SyncStatus = "pending";
    }

    public void MarkActive(decimal balance)
    {
        if (SyncStatus != "syncing")
            throw new InvalidOperationException(
                $"Cannot mark account active from status '{SyncStatus}'.");
        SyncStatus = "active";
        CurrentBalance = balance;
        LastSyncError = null;
    }

    public void MarkFailed(string? errorCode = null)
    {
        if (SyncStatus != "syncing")
            throw new InvalidOperationException(
                $"Cannot mark account failed from status '{SyncStatus}'.");
        SyncStatus = "failed";
        LastSyncError = errorCode;
    }

    /// <summary>
    /// Restores a syncing account to active after a transient, self-healing failure
    /// (e.g. a provider rate-limit / 429). Unlike <see cref="MarkFailed"/> this leaves
    /// no error state and raises no user-facing alert — the connection is valid and the
    /// next scheduled cycle will retry. Balance is left untouched.
    /// </summary>
    public void MarkTransientRetry()
    {
        if (SyncStatus != "syncing")
            throw new InvalidOperationException(
                $"Cannot mark account transient-retry from status '{SyncStatus}'.");
        SyncStatus = "active";
        LastSyncError = null;
    }

    public void BeginSync()
    {
        if (SyncStatus == "syncing")
            throw new InvalidOperationException($"Account {Id} is already syncing.");
        SyncStatus = "syncing";
    }

    public void MarkReauthRequired()
    {
        SyncStatus = "reauth_required";
    }

    /// <summary>
    /// Heals an account after a successful reconnect/reauth. Unlike <see cref="MarkActive"/>,
    /// which only transitions from "syncing", this transitions from <b>any</b> status (notably
    /// "reauth_required"), re-points the account at the freshly linked TrueLayer connection, and
    /// clears the stale error (and the last failure on its health) so the scheduler resumes syncing it on the next cycle.
    /// </summary>
    public void MarkReconnected(Guid trueLayerConnectionId, decimal balance)
    {
        TrueLayerConnectionId = trueLayerConnectionId;
        SyncStatus = "active";
        CurrentBalance = balance;
        LastSyncError = null;
        // The state is kept so the first success still resolves the open alert, but the old failure no longer times a
        // Dormant account's slowed cadence: the next scheduled run tries it.
        Health = Health with { LastFailureAt = null };
    }

    /// <summary>
    /// Writes the identifiers a discovery read lists onto an existing row and refreshes the last four digits.
    /// A null argument leaves its field alone (the provider did not list it). Returns whether anything changed.
    /// </summary>
    public bool RefreshProviderIdentifiers(string? iban, string[]? maskedPans, string? last4, string? cardValidTo)
    {
        var changed = false;
        if (iban is not null && iban != Iban) { Iban = iban; changed = true; }
        if (maskedPans is { Length: > 0 } && !maskedPans.SequenceEqual(MaskedPans ?? [])) { MaskedPans = maskedPans; changed = true; }
        if (cardValidTo is not null && cardValidTo != CardValidTo) { CardValidTo = cardValidTo; changed = true; }
        if (last4 is { Length: 4 } && last4.All(char.IsDigit) && last4 != AccountNumberLast4) { AccountNumberLast4 = last4; changed = true; }
        return changed;
    }

    public void ValidateInvariants()
    {
        if (string.IsNullOrWhiteSpace(ExternalAccountId))
            throw new ArgumentException("ExternalAccountId cannot be empty");
        if (string.IsNullOrWhiteSpace(BankName))
            throw new ArgumentException("BankName cannot be empty");
        if (string.IsNullOrWhiteSpace(AccountType))
            throw new ArgumentException("AccountType cannot be empty");
        if (AccountNumberLast4.Length != 4)
            throw new ArgumentException("AccountNumberLast4 must be exactly 4 characters");
    }

    /// <summary>
    /// This account's own health by the connection-health policy, recorded on each of its syncs. It alerts only
    /// for a failure its connection is not charged with (see <c>BankConnectionHealth</c>, Option B, S2).
    /// </summary>
    public ConnectionHealth Health { get; private set; } = new();

    public void ApplyHealth(ConnectionHealth health) => Health = health;
}

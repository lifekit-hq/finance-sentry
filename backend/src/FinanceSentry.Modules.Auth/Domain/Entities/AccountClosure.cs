namespace FinanceSentry.Modules.Auth.Domain.Entities;

/// <summary>
/// The audit record of a person closing their own account. Closing locks the account, ends every session and deletes
/// the stored provider keys at once; the rest of the person's data stays, unreachable, until <see cref="PurgeDueAt"/>,
/// when the purge job erases it and deletes the account. Until then an Owner can reopen the account.
/// <para>
/// The record outlives the account: it is keyed by the person's id with no foreign key to the user row, holds no
/// email or name, and the purge never deletes it.
/// </para>
/// </summary>
public class AccountClosure
{
    /// <summary>How long a closed account's data is kept, reopenable, before the purge erases it.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public DateTime ClosedAt { get; private set; }
    public DateTime PurgeDueAt { get; private set; }

    /// <summary>JSON: what closing removed per module (provider keys, sync jobs) and any provider revoke that failed.</summary>
    public string? CredentialRevocation { get; private set; }

    public DateTime? ReopenedAt { get; private set; }
    public Guid? ReopenedByUserId { get; private set; }
    public DateTime? PurgedAt { get; private set; }

    /// <summary>JSON: rows the purge erased per module.</summary>
    public string? ErasedRows { get; private set; }

    private AccountClosure() { }

    public AccountClosure(Guid userId, DateTime closedAt)
    {
        UserId = userId;
        ClosedAt = closedAt;
        PurgeDueAt = closedAt + GracePeriod;
    }

    /// <summary>Neither reopened nor purged: the account is closed and its data still held.</summary>
    public bool IsPending => ReopenedAt is null && PurgedAt is null;

    public void RecordCredentialRevocation(string json) => CredentialRevocation = json;

    public void Reopen(Guid reopenedByUserId, DateTime at)
    {
        ReopenedAt = at;
        ReopenedByUserId = reopenedByUserId;
    }

    public void MarkPurged(DateTime at, string erasedRowsJson)
    {
        PurgedAt = at;
        ErasedRows = erasedRowsJson;
    }
}

namespace FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Repository for the merchants a user pinned as committed — rule (d) of
/// <c>CommittedOutflowRules</c>. Its own file rather than a further entry in
/// <c>IRepositories.cs</c>: that file is already a catch-all, and one aggregate's port is the
/// unit a reader looks for.
/// </summary>
public interface ICommittedMerchantPinRepository
{
    /// <summary>Every pin the user holds, ordered by merchant key so listings are stable.</summary>
    Task<IReadOnlyList<CommittedMerchantPin>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Just the keys, as the set the policy tests membership against. Separate from
    /// <see cref="ListAsync"/> because the policy runs it per statistics request and needs no
    /// display names.
    /// </summary>
    Task<IReadOnlySet<string>> GetPinnedKeysAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the pin unless the user already holds one for its key, and reports which happened.
    /// One operation rather than a find followed by an add: the two are racy against the unique
    /// <c>(UserId, MerchantKey)</c> index — two tabs pinning one merchant would fail the second
    /// write — and only the implementation knows what that collision looks like.
    /// </summary>
    Task<AddCommittedMerchantPinResult> AddIfAbsentAsync(
        CommittedMerchantPin pin, CancellationToken cancellationToken = default);

    /// <summary>False when the user held no pin for that key — the caller reports "not pinned".</summary>
    Task<bool> RemoveAsync(
        Guid userId, string merchantKey, CancellationToken cancellationToken = default);
}

/// <param name="Pin">
/// The pin the user now holds for the key — the row that was stored, or the one that was
/// already there.
/// </param>
/// <param name="AlreadyPinned">True when the user held the pin before this call.</param>
public sealed record AddCommittedMerchantPinResult(CommittedMerchantPin Pin, bool AlreadyPinned);

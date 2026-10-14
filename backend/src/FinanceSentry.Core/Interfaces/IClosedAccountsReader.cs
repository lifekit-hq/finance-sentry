namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// The people whose account is closed and waiting for its data to be erased. Background work that enumerates people
/// from a module's own rows skips them: a closed account's data is kept, reopenable, but nothing runs for it.
/// </summary>
public interface IClosedAccountsReader
{
    Task<IReadOnlySet<Guid>> GetClosedUserIdsAsync(CancellationToken ct = default);
}

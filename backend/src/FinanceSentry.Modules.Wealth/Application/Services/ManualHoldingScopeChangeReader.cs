namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// A value entered by hand joins the scope the way a newly linked account does (framework §3.2 rule 3): its value
/// existed before it was typed in, so a change across the day it was added is not growth. Removing it is the mirror:
/// no money left, so a change across the day it was removed is not a decline.
/// </summary>
public sealed class ManualHoldingScopeChangeReader(IManualHoldingRepository holdings) : IAccountScopeChangeReader
{
    public string WealthSourceId => WealthSourceIds.Manual;

    public async Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(
        Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        var changes = new List<AccountScopeChange>();
        foreach (var h in await holdings.ListIncludingRemovedUnscopedAsync(userId, ct))
        {
            if (h.CreatedAt.UtcDateTime >= sinceUtc)
                changes.Add(new AccountScopeChange(WealthSourceIds.Manual, AccountScopeChangeKinds.Linked, h.CreatedAt.UtcDateTime));

            if (h.RemovedAt is { } removedAt && removedAt.UtcDateTime >= sinceUtc)
                changes.Add(new AccountScopeChange(WealthSourceIds.Manual, AccountScopeChangeKinds.Removed, removedAt.UtcDateTime));
        }

        return changes;
    }
}

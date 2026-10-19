namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// A value entered by hand joins the scope the way a newly linked account does (framework §3.2 rule 3): its value
/// existed before it was typed in, so a change across the day it was added is not growth.
/// </summary>
public sealed class ManualHoldingScopeChangeReader(IManualHoldingRepository holdings) : IAccountScopeChangeReader
{
    // shortcut: a removed entry is deleted outright, so its removal is not reported; record a removal time if a
    // window spanning a removal needs to withhold its percentage too.
    public async Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(
        Guid userId, DateTime sinceUtc, CancellationToken ct = default)
        => [.. (await holdings.ListUnscopedAsync(userId, ct))
            .Where(h => h.CreatedAt.UtcDateTime >= sinceUtc)
            .Select(h => new AccountScopeChange(WealthSourceIds.Manual, AccountScopeChangeKinds.Linked, h.CreatedAt.UtcDateTime))];
}

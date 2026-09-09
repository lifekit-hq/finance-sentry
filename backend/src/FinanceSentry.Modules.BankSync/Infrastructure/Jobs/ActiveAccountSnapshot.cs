namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>The identity, owner and billing currency of one active account.</summary>
public sealed record ActiveAccount(Guid AccountId, Guid UserId, string Currency);

/// <summary>
/// The 044 sentinels' shared liveness policy, read once: only transactions on active accounts
/// participate, because a disconnected account's history must not raise new alerts. Every sentinel
/// also needs the owning account's currency to normalise or partition amounts, so the same read
/// answers both — and the policy is stated in one place rather than restated per job.
/// </summary>
public sealed class ActiveAccountSnapshot
{
    private readonly Dictionary<Guid, string> _currencyByAccount;

    private ActiveAccountSnapshot(IReadOnlyList<ActiveAccount> accounts)
    {
        Accounts = accounts;
        _currencyByAccount = accounts.ToDictionary(a => a.AccountId, a => a.Currency);
        AccountIds = [.. _currencyByAccount.Keys];
    }

    public IReadOnlyList<ActiveAccount> Accounts { get; }

    /// <summary>Materialised for EF's <c>Contains</c> translation in the callers' transaction queries.</summary>
    public List<Guid> AccountIds { get; }

    public static async Task<ActiveAccountSnapshot> ReadAsync(BankSyncDbContext db, CancellationToken ct)
    {
        var accounts = await db.BankAccounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new ActiveAccount(a.Id, a.UserId, a.Currency))
            .ToListAsync(ct);

        return new ActiveAccountSnapshot(accounts);
    }

    /// <summary>
    /// The billing currency of an account this snapshot covers. Callers only ever ask about accounts
    /// whose rows they selected through <see cref="AccountIds"/>, so a miss is a broken caller, not a
    /// data case — it throws rather than converting someone's hryvnia as if it were dollars.
    /// </summary>
    public string CurrencyOf(Guid accountId) => _currencyByAccount[accountId];
}

namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public class BankingTotalsReader(
    IBankAccountRepository accounts,
    ISyncJobRepository syncJobs) : IBankingTotalsReader
{
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ISyncJobRepository _syncJobs = syncJobs ?? throw new ArgumentNullException(nameof(syncJobs));

    public async Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct = default)
    {
        var active = await _accounts.GetAllActiveUnscopedAsync(ct);
        return active.Select(a => a.UserId).Distinct().ToList();
    }

    public async Task<decimal> GetTotalUsdAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await _accounts.GetByUserIdUnscopedAsync(userId, ct);
        return accounts
            .Where(a => a.CurrentBalance.HasValue)
            .Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, CurrencyConverter.ToUsd(a.CurrentBalance!.Value, a.Currency)));
    }

    public async Task<DateTime?> GetLatestSuccessfulSyncAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await _accounts.GetByUserIdUnscopedAsync(userId, ct);
        var activeIds = accounts.Select(a => a.Id).ToHashSet();
        if (activeIds.Count == 0)
            return null;

        var lastSuccessful = await _syncJobs.GetLastSuccessfulSyncTimesByUserUnscopedAsync(userId, ct);
        var times = lastSuccessful
            .Where(kv => activeIds.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        return times.Count > 0 ? times.Max() : null;
    }
}

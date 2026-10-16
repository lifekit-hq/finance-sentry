namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

using FinanceSentry.Modules.BankSync.Domain.Repositories;

public class BankingAccountsReader(
    IBankAccountRepository accounts,
    ISyncJobRepository syncJobs,
    IClosedAccountsReader closedAccounts,
    TimeProvider? clock = null) : IBankingAccountsReader
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ISyncJobRepository _syncJobs = syncJobs ?? throw new ArgumentNullException(nameof(syncJobs));
    private readonly IClosedAccountsReader _closedAccounts = closedAccounts;

    // Only accounts whose balance counts in live totals: every consumer sums these, and a retired or vanished
    // account's balance has moved on (to its successor, or to nothing).
    public async Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        return await SummariesAsync(userId, a => a.CountsInLiveTotals(now), ct);
    }

    // The retired and vanished accounts, which keep their history: a reader labelling old transactions needs their
    // currency and provider, and must never sum their balance.
    public async Task<IReadOnlyList<BankingAccountSummary>> GetNonLiveAccountSummariesAsync(Guid userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        return await SummariesAsync(userId, a => !a.CountsInLiveTotals(now), ct);
    }

    private async Task<IReadOnlyList<BankingAccountSummary>> SummariesAsync(
        Guid userId, Func<Domain.BankAccount, bool> include, CancellationToken ct)
    {
        var list = (await _accounts.GetByUserIdUnscopedAsync(userId, ct)).Where(include).ToList();
        var lastSuccessful = await _syncJobs.GetLastSuccessfulSyncTimesByUserUnscopedAsync(userId, ct);

        return list.Select(a =>
        {
            // A balance that is not reported has nothing to convert, so nothing was substituted.
            ConvertResult? usd = a.CurrentBalance.HasValue
                ? CurrencyConverter.Convert(a.CurrentBalance.Value, a.Currency)
                : null;

            return new BankingAccountSummary(
                a.Id,
                a.BankName,
                a.AccountType,
                a.AccountNumberLast4,
                a.Provider,
                a.Currency,
                a.CurrentBalance,
                usd?.Amount,
                a.SyncStatus == "active" ? "synced" : a.SyncStatus,
                a.UpdatedAt,
                lastSuccessful.TryGetValue(a.Id, out var lastOk) ? lastOk : null,
                a.MonobankCredentialId,
                a.TrueLayerConnectionId,
                a.ProductType,
                usd?.Status ?? FxStatus.Exact);
        })
        .ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default)
    {
        var all = await _accounts.GetAllActiveUnscopedAsync(ct);
        var closed = await _closedAccounts.GetClosedUserIdsAsync(ct);
        return all.Select(a => a.UserId).Distinct().Where(id => !closed.Contains(id)).ToList();
    }

    public async Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var list = await _accounts.GetByUserIdUnscopedAsync(userId, ct);
        return list
            .Where(a => a.CountsInLiveTotals(now))
            .Select(a => new AccountBalanceSnapshot(a.Id, a.BankName, a.AccountType, a.AccountNumberLast4, a.Currency, a.CurrentBalance))
            .ToList();
    }
}

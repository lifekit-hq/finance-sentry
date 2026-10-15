namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

using FinanceSentry.Modules.BankSync.Domain.Repositories;

public class BankingAccountsReader(
    IBankAccountRepository accounts,
    ISyncJobRepository syncJobs,
    IClosedAccountsReader closedAccounts) : IBankingAccountsReader
{
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ISyncJobRepository _syncJobs = syncJobs ?? throw new ArgumentNullException(nameof(syncJobs));
    private readonly IClosedAccountsReader _closedAccounts = closedAccounts;

    public async Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _accounts.GetByUserIdUnscopedAsync(userId, ct);
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
        var list = await _accounts.GetByUserIdUnscopedAsync(userId, ct);
        return list
            .Select(a => new AccountBalanceSnapshot(a.Id, a.BankName, a.AccountType, a.AccountNumberLast4, a.Currency, a.CurrentBalance))
            .ToList();
    }
}

namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public class BankingTransactionReader(
    IBankAccountRepository accounts,
    ITransactionRepository transactions) : IBankingTransactionReader
{
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ITransactionRepository _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));

    public async Task<IReadOnlyList<BankingTransactionSummary>> GetTransactionsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var accountList = await _accounts.GetByUserIdUnscopedAsync(userId, ct);
        var providerByAccount = accountList.ToDictionary(a => a.Id, a => a.Provider);
        var currencyByAccount = accountList.ToDictionary(a => a.Id, a => a.Currency);

        var txList = await _transactions.GetByUserIdUnscopedAsync(userId, ct);

        return txList
            .Where(t => t.IsActive)
            .Select(t =>
            {
                var effectiveDate = t.PostedDate ?? t.TransactionDate;
                return new { t, effectiveDate };
            })
            .Where(x => x.effectiveDate >= fromUtc && x.effectiveDate <= toUtc)
            .Select(x =>
            {
                var currency = currencyByAccount.TryGetValue(x.t.AccountId, out var c) ? c : "USD";
                return new BankingTransactionSummary(
                    x.t.AccountId,
                    providerByAccount.TryGetValue(x.t.AccountId, out var p) ? p : "unknown",
                    x.t.TransactionType ?? "debit",
                    x.t.Amount,
                    currency,
                    CurrencyConverter.ToUsd(x.t.Amount, currency),
                    x.effectiveDate,
                    x.t.IsPending);
            })
            .ToList();
    }
}

namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>Per-category spend for a window, keyed by merchant category, summed in USD.</summary>
public record GetMerchantSpendingQuery(Guid UserId, DateOnly From, DateOnly To)
    : IQuery<IReadOnlyDictionary<string, decimal>>;

public class GetMerchantSpendingQueryHandler(
    ITransactionRepository transactions,
    IBankAccountRepository accounts)
    : IQueryHandler<GetMerchantSpendingQuery, IReadOnlyDictionary<string, decimal>>
{
    private readonly ITransactionRepository _transactions = transactions;
    private readonly IBankAccountRepository _accounts = accounts;

    public async Task<IReadOnlyDictionary<string, decimal>> Handle(
        GetMerchantSpendingQuery request, CancellationToken cancellationToken)
    {
        var fromUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = request.To.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var all = await _transactions.GetByUserIdUnscopedAsync(request.UserId, cancellationToken);

        // Convert to USD by account currency before summing — spend feeds budget comparisons,
        // which mix accounts in different currencies.
        var accountList = await _accounts.GetByUserIdUnscopedAsync(request.UserId, cancellationToken);
        var currencyByAccount = accountList.ToDictionary(a => a.Id, a => a.Currency);

        var result = all
            .Where(t =>
                t.IsActive &&
                t.TransactionType == "debit" &&
                t.PostedDate.HasValue &&
                t.PostedDate.Value >= fromUtc &&
                t.PostedDate.Value <= toUtc)
            .GroupBy(t => t.MerchantCategory ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(t => CurrencyConverter.ToUsd(
                    t.Amount, currencyByAccount.TryGetValue(t.AccountId, out var c) ? c : "USD")));

        return result;
    }
}

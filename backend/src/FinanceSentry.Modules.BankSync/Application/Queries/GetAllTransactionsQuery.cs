namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record GlobalTransactionDto(
    Guid TransactionId,
    Guid AccountId,
    string BankName,
    string Currency,
    decimal Amount,
    decimal AmountUsd,
    DateTime Date,
    DateTime? PostedDate,
    string Description,
    string? TransactionType,
    string? MerchantCategory,
    string? MerchantName,
    bool IsPending,
    DateTime CreatedAt);

public record AllTransactionsResult(
    IReadOnlyList<GlobalTransactionDto> Transactions,
    int TotalCount,
    bool HasMore,
    int Offset,
    int Limit);

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// The trailing filter fields (from <see cref="AccountIds"/> on) are additive — every field
/// after <see cref="TransactionType"/> must keep a default and never change meaning, since the
/// MCP <c>list_transactions</c> tool constructs this positionally.
/// </summary>
public record GetAllTransactionsQuery(
    Guid UserId,
    PagedRequest Paging,
    DateTime? From = null,
    DateTime? To = null,
    string? TransactionType = null,
    IReadOnlyList<Guid>? AccountIds = null,
    IReadOnlyList<string>? Categories = null,
    decimal? MinAmountUsd = null,
    decimal? MaxAmountUsd = null,
    string? Search = null
) : IQuery<AllTransactionsResult>;

// ── Handler ──────────────────────────────────────────────────────────────────

public class GetAllTransactionsQueryHandler(
    ITransactionRepository transactions,
    IBankAccountRepository accounts)
    : IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult>
{
    private readonly ITransactionRepository _transactions = transactions;
    private readonly IBankAccountRepository _accounts = accounts;

    public async Task<AllTransactionsResult> Handle(GetAllTransactionsQuery request, CancellationToken ct)
    {
        var accountList = (await _accounts.GetByUserIdAsync(request.UserId, ct)).ToList();
        var accountMap = accountList.ToDictionary(a => a.Id, a => (a.BankName, a.Currency));

        var filter = new TransactionFilter(
            AccountIds: request.AccountIds,
            Categories: request.Categories,
            From: request.From,
            To: request.To,
            TransactionType: request.TransactionType,
            Search: request.Search,
            AmountRanges: BuildAmountRanges(request.MinAmountUsd, request.MaxAmountUsd, accountList));

        var (items, totalCount) = await _transactions.GetFilteredByUserIdAsync(
            request.UserId, filter, request.Paging.Offset, request.Paging.Limit, ct);

        var dtos = items.Select(t =>
        {
            var meta = accountMap.TryGetValue(t.AccountId, out var m) ? m : ("Unknown", "USD");
            return new GlobalTransactionDto(
                t.Id,
                t.AccountId,
                meta.Item1,
                meta.Item2,
                t.Amount,
                CurrencyConverter.ToUsd(t.Amount, meta.Item2),
                t.TransactionDate,
                t.PostedDate,
                t.Description,
                t.TransactionType,
                t.MerchantCategory,
                t.MerchantName,
                t.IsPending,
                t.CreatedAt);
        }).ToList();

        return new AllTransactionsResult(
            dtos, totalCount, request.Paging.Offset + dtos.Count < totalCount, request.Paging.Offset, request.Paging.Limit);
    }

    /// <summary>
    /// Translates a USD-normalised amount range into one native bound per currency the user
    /// holds accounts in — <c>CurrencyConverter</c> keeps rates in-process, not in a SQL-visible
    /// table, so the OR-of-currency-groups translation has to happen here rather than in the
    /// repository (see docs/money-semantics.md).
    /// </summary>
    private static IReadOnlyList<AccountAmountRange>? BuildAmountRanges(
        decimal? minUsd, decimal? maxUsd, IReadOnlyList<Domain.BankAccount> accounts)
    {
        if (minUsd is null && maxUsd is null)
            return null;

        var ranges = new List<AccountAmountRange>();
        foreach (var group in accounts.GroupBy(a => a.Currency, StringComparer.OrdinalIgnoreCase))
        {
            var rate = CurrencyConverter.ToUsd(1m, group.Key);
            var nativeMin = minUsd.HasValue ? minUsd.Value / rate : (decimal?)null;
            var nativeMax = maxUsd.HasValue ? maxUsd.Value / rate : (decimal?)null;
            ranges.Add(new AccountAmountRange(group.Select(a => a.Id).ToList(), nativeMin, nativeMax));
        }

        return ranges;
    }
}

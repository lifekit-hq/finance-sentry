namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Utils;

// ──────────────────────────────────────────────
// Query
// ──────────────────────────────────────────────

/// <summary>
/// Returns all active bank accounts for the authenticated user.
/// Optionally filter by sync status or currency.
/// FR-009: All queries MUST be scoped to the authenticated user.
/// </summary>
public record GetAccountsQuery(
    Guid UserId,
    string? SyncStatusFilter = null,
    string? CurrencyFilter = null
) : IQuery<GetAccountsResult>;

// ──────────────────────────────────────────────
// Result DTO
// ──────────────────────────────────────────────

public record GetAccountsResult(
    IReadOnlyList<BankAccountDto> Accounts,
    int TotalCount,
    IReadOnlyDictionary<string, decimal> CurrencyTotals
);

public record BankAccountDto(
    Guid AccountId,
    string BankName,
    string AccountType,
    string AccountNumberLast4,
    string Currency,
    decimal? CurrentBalance,
    decimal? AvailableBalance,
    decimal? CreditLimit,
    string SyncStatus,
    DateTime? LastSyncTimestamp,
    DateTime? LastSuccessfulSyncTimestamp,
    DateTime CreatedAt,
    string Provider,
    DateTimeOffset? ClosedAt = null,
    string? ClosedReason = null,
    Guid? PredecessorAccountId = null,
    DateTimeOffset? AbsentSince = null,
    string? ProductType = null,
    string? CardValidTo = null,
    string? PreviousLast4 = null,
    DateTimeOffset? PanChangedAt = null
);

// ──────────────────────────────────────────────
// Handler
// ──────────────────────────────────────────────

public class GetAccountsQueryHandler(
    IBankAccountRepository accounts,
    ISyncJobRepository syncJobs,
    TimeProvider? clock = null) : IQueryHandler<GetAccountsQuery, GetAccountsResult>
{
    private readonly IBankAccountRepository _accounts = accounts;
    private readonly ISyncJobRepository _syncJobs = syncJobs;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<GetAccountsResult> Handle(
          GetAccountsQuery request, CancellationToken cancellationToken)
    {
        var accounts = await _accounts.GetByUserIdAsync(request.UserId, cancellationToken);
        var lastSuccessful = await _syncJobs.GetLastSuccessfulSyncTimesByUserAsync(
            request.UserId, cancellationToken);

        // Apply optional filters
        IEnumerable<BankAccount> filtered = accounts;

        if (!string.IsNullOrWhiteSpace(request.SyncStatusFilter))
            filtered = filtered.Where(a => a.SyncStatus == request.SyncStatusFilter);

        if (!string.IsNullOrWhiteSpace(request.CurrencyFilter))
            filtered = filtered.Where(a => a.Currency == request.CurrencyFilter.ToUpperInvariant());

        var list = filtered.ToList();

        var dtos = list.Select(a => new BankAccountDto(
            a.Id,
            a.BankName,
            a.AccountType,
            a.AccountNumberLast4,
            a.Currency,
            a.CurrentBalance,
            null,
            a.CreditLimit,
            a.SyncStatus,
            a.UpdatedAt,
            lastSuccessful.TryGetValue(a.Id, out var lastOk) ? lastOk : null,
            a.CreatedAt,
            a.Provider,
            a.ClosedAt,
            a.ClosedReason,
            a.PredecessorAccountId,
            a.AbsentSince,
            a.ProductType,
            a.CardValidTo,
            a.PreviousLast4,
            a.PanChangedAt
        )).ToList();

        // Aggregate current balances by currency (only accounts with known balance that count in live totals: a
        // retired or vanished account is listed, but its balance is not added)
        var now = _clock.GetUtcNow();
        var currencyTotals = list
            .Where(a => a.CurrentBalance.HasValue && a.CountsInLiveTotals(now))
            .GroupBy(a => a.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, a.CurrentBalance!.Value)));

        return new GetAccountsResult(dtos, dtos.Count, currencyTotals);
    }
}

namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Modules.Wealth.Domain.Services;

public record AppliedFiltersDto(string? Category, string? Provider);

/// <summary>
/// A single sub-account under an institution: a Monobank card, a Revolut
/// currency pocket, a Binance asset, an IBKR position. <see cref="NativeValue"/> in
/// <see cref="NativeCurrency"/> is a position's value as its provider reported it (an Inzhur fund in
/// UAH); null for rows whose <see cref="CurrentBalance"/> is already their native amount.
/// </summary>
public record AccountBalanceDto(
    Guid AccountId,
    string BankName,
    string AccountType,
    string AccountNumberLast4,
    string Provider,
    string Category,
    string Currency,
    decimal? CurrentBalance,
    decimal? BalanceInBaseCurrency,
    string SyncStatus,
    DateTime? LastSyncTimestamp,
    string? ProductType = null,
    string? NativeCurrency = null,
    decimal? NativeValue = null,
    string? Symbol = null);

/// <summary>
/// A physical card within an institution, grouping its per-currency sub-accounts. Populated
/// only for providers that expose a card product (Monobank black/white/…); null elsewhere.
/// </summary>
public record CardGroupDto(
    string CardType,
    string DisplayName,
    decimal TotalInBaseCurrency,
    string SyncStatus,
    IReadOnlyList<AccountBalanceDto> Accounts);

/// <summary>
/// An institution the user has connected: one Monobank customer, one
/// TrueLayer connection (Revolut, AIB), one Binance account, one IBKR account.
/// Every disconnect flow operates at this level.
/// </summary>
public record InstitutionDto(
    Guid InstitutionId,
    string Provider,
    string Name,
    string Category,
    decimal TotalInBaseCurrency,
    string SyncStatus,
    DateTime? LastSyncTimestamp,
    DateTime? LastSuccessfulSyncTimestamp,
    IReadOnlyList<AccountBalanceDto> Accounts,
    IReadOnlyList<CardGroupDto>? Cards = null);

public record CategorySummaryDto(
    string Category,
    decimal TotalInBaseCurrency,
    int InstitutionCount,
    IReadOnlyList<InstitutionDto> Institutions);

public record WealthSummaryResponse(
    decimal TotalNetWorth,
    string BaseCurrency,
    IReadOnlyList<CategorySummaryDto> Categories,
    AppliedFiltersDto AppliedFilters,
    Headline? Headline = null);

public record GetWealthSummaryQuery(
    Guid UserId,
    string? Category = null,
    string? Provider = null) : IQuery<WealthSummaryResponse>;

public class GetWealthSummaryQueryHandler(IWealthAggregationService service)
    : IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>
{
    private readonly IWealthAggregationService _service = service;

    public Task<WealthSummaryResponse> Handle(GetWealthSummaryQuery request, CancellationToken cancellationToken)
        => _service.GetWealthSummaryAsync(request.UserId, request.Category, request.Provider, cancellationToken);
}

namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Core.Cqrs;

// ── Query ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns the top spending categories for a user over the last <see cref="Months"/>
/// calendar months, sorted by total spend DESC.
/// </summary>
public record GetTopCategoriesQuery(Guid UserId, int Limit = 10, int Months = 6) : IQuery<IReadOnlyList<CategoryStat>>;

// ── Handler ────────────────────────────────────────────────────────────────────

public class GetTopCategoriesQueryHandler(
    IMerchantCategoryStatisticsService service,
    ICounterpartyClassificationService classification) : IQueryHandler<GetTopCategoriesQuery, IReadOnlyList<CategoryStat>>
{
    private readonly IMerchantCategoryStatisticsService _service = service;
    private readonly ICounterpartyClassificationService _classification = classification;

    public async Task<IReadOnlyList<CategoryStat>> Handle(
          GetTopCategoriesQuery request, CancellationToken cancellationToken)
    {
        var counterparties = await _classification.ClassifyForWindowAsync(
            request.UserId, request.Months, cancellationToken);

        return await _service.GetTopCategoriesAsync(
            request.UserId, counterparties, request.Limit, request.Months, ct: cancellationToken);
    }
}

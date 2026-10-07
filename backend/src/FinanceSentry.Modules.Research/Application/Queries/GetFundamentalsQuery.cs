namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;

public record GetFundamentalsQuery(
    string Ticker,
    int MaxPerConcept) : IQuery<IReadOnlyList<FundamentalFactDto>>;

public class GetFundamentalsQueryHandler(ISecEdgarService svc)
    : IQueryHandler<GetFundamentalsQuery, IReadOnlyList<FundamentalFactDto>>
{
    public async Task<IReadOnlyList<FundamentalFactDto>> Handle(GetFundamentalsQuery query, CancellationToken ct)
    {
        var facts = await svc.GetFundamentalsAsync(query.Ticker, query.MaxPerConcept, ct);
        return facts
            .Select(f => new FundamentalFactDto(
                f.Ticker, f.Concept, f.Label, f.Unit, f.Value,
                f.PeriodEnd, f.FiscalPeriod, f.FiscalYear, f.Form, f.Taxonomy))
            .ToList();
    }
}

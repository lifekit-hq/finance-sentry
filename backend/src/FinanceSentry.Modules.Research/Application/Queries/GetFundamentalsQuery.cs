namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;

public record GetFundamentalsQuery(
    string Ticker,
    int MaxPerConcept) : IQuery<FundamentalsDto>;

public class GetFundamentalsQueryHandler(IFundamentalsService fundamentals)
    : IQueryHandler<GetFundamentalsQuery, FundamentalsDto>
{
    public async Task<FundamentalsDto> Handle(GetFundamentalsQuery query, CancellationToken ct)
    {
        var result = await fundamentals.GetFundamentalsAsync(query.Ticker, query.MaxPerConcept, ct);
        return new FundamentalsDto(
            result.Ticker,
            result.Coverage,
            result.Facts
                .Select(f => new FundamentalFactDto(
                    f.Ticker, f.Concept, f.Label, f.Unit, f.Value,
                    f.PeriodEnd, f.FiscalPeriod, f.FiscalYear, f.Form, f.Taxonomy, f.SourceProvenance))
                .ToList());
    }
}

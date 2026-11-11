namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Scoring;

public record GetFundamentalsQuery(
    string Ticker,
    int MaxPerConcept) : IQuery<FundamentalsDto>;

public class GetFundamentalsQueryHandler(IFundamentalsService fundamentals)
    : IQueryHandler<GetFundamentalsQuery, FundamentalsDto>
{
    private const int MinPerConcept = 1;
    private const int MaxPerConcept = 20;
    private const int YoyLookbackPeriods = 4;
    private const int MinQuarters = 5;

    public async Task<FundamentalsDto> Handle(GetFundamentalsQuery query, CancellationToken ct)
    {
        // Read extra history so the oldest requested period still has a year-earlier period to compare with.
        var requested = Math.Clamp(query.MaxPerConcept, MinPerConcept, MaxPerConcept);
        var result = await fundamentals.GetFundamentalsAsync(
            query.Ticker, Math.Min(requested + YoyLookbackPeriods, MaxPerConcept), ct);

        var fcf = DerivedFundamentalsMath.FreeCashFlow(result.Facts, Math.Max(requested, MinQuarters), requested);
        var roe = DerivedFundamentalsMath.ReturnOnEquity(result.Facts, requested);
        var shown = result.Facts
            .GroupBy(f => f.Concept)
            .SelectMany(g => g.OrderByDescending(f => f.PeriodEnd).Take(requested))
            .OrderBy(f => f.Concept, StringComparer.Ordinal)
            .ThenByDescending(f => f.PeriodEnd)
            .ToList();

        return new FundamentalsDto(
            result.Ticker,
            result.Coverage,
            shown
                .Select(f => new FundamentalFactDto(
                    f.Ticker, f.Concept, f.Label, f.Unit, f.Value,
                    f.PeriodEnd, f.FiscalPeriod, f.FiscalYear, f.Form, f.Taxonomy, f.SourceProvenance))
                .ToList(),
            fcf,
            fcf.Count > 0 ? null : MissingReason(result, "cash flow (operating cash flow and capex)"),
            roe,
            roe.Count > 0 ? null : MissingReason(result, "net income and stockholders' equity"));
    }

    private static string MissingReason(FundamentalsResult result, string what)
        => result.Coverage.Status == FundamentalsCoverageStatus.Covered
            ? $"no annual or quarterly {what} facts from {string.Join(", ", result.Coverage.Bases.Select(b => b.Provider).Distinct())} (Yahoo-sourced bases carry no cash flow; EDGAR files none for this issuer)"
            : $"no fundamentals: {result.Coverage.Reason ?? result.Coverage.Status}";
}

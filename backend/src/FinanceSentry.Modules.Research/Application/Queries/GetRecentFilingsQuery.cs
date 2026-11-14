namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Ports;

public record GetRecentFilingsQuery(
    string Ticker,
    IReadOnlyList<string>? FormTypes,
    int Limit) : IQuery<IReadOnlyList<EdgarFilingDto>>;

public class GetRecentFilingsQueryHandler(ISecEdgarService svc)
    : IQueryHandler<GetRecentFilingsQuery, IReadOnlyList<EdgarFilingDto>>
{
    public async Task<IReadOnlyList<EdgarFilingDto>> Handle(GetRecentFilingsQuery query, CancellationToken ct)
    {
        // A provider failure (map_unavailable / source_unavailable) is an error, not an empty list: an
        // empty result means "not a filer / nothing filed" (#1375).
        IReadOnlyList<Domain.EdgarFiling> filings;
        try
        {
            filings = await svc.GetRecentFilingsAsync(query.Ticker, query.FormTypes, query.Limit, ct, surfaceProviderFailure: true);
        }
        catch (EdgarProviderException ex)
        {
            throw new EdgarSourceUnavailableException(ex);
        }

        return filings
            .Select(f => new EdgarFilingDto(
                f.Ticker, f.Form, f.FilingDate, f.ReportDate, f.Description,
                f.AccessionNumber, f.DocumentUrl, f.IsXbrl))
            .ToList();
    }
}

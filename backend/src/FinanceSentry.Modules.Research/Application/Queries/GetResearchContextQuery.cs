namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

public record GetResearchContextQuery(
    Guid UserId,
    Guid? ThesisId,
    string? Ticker,
    string? Question,
    DateTimeOffset? From,
    int? MaxChunks,
    IReadOnlyList<ResearchDocumentSourceType> IncludeSourceTypes) : IQuery<ResearchContextPacketDto>;

public class GetResearchContextQueryHandler(
    IResearchRetriever retriever,
    IThesisRepository theses,
    IOptions<ResearchRetrievalOptions> options)
    : IQueryHandler<GetResearchContextQuery, ResearchContextPacketDto>
{
    private const int SnippetLength = 300;
    private const int ThesisSummaryLength = 500;

    private static readonly string[] GroupOrder =
        ["thesis", "decision_notes", "recent_news", "filings", "postmortems", "other_research"];

    public async Task<ResearchContextPacketDto> Handle(GetResearchContextQuery query, CancellationToken ct)
    {
        var opts = options.Value;
        var subjectType = query.ThesisId is not null ? "Thesis" : "Ticker";
        var thesis = await ResolveThesisAsync(query, ct);
        var ticker = thesis?.Ticker ?? query.Ticker?.ToUpperInvariant();
        var maxChunks = Math.Clamp(query.MaxChunks ?? opts.ContextMaxChunks, 1, opts.ContextMaxChunksCap);

        var searchText = !string.IsNullOrWhiteSpace(query.Question)
            ? query.Question
            : thesis is not null
                ? Truncate(thesis.ThesisText, ThesisSummaryLength)
                : $"{ticker} research evidence and recent developments";

        var result = await retriever.SearchAsync(
            new ResearchRetrievalRequest(
                query.UserId,
                searchText,
                ticker is not null ? [ticker] : [],
                ticker is null ? query.ThesisId : null,
                query.IncludeSourceTypes,
                query.From,
                To: null,
                maxChunks),
            ct);

        var groups = result.Hits
            .GroupBy(h => GroupName(h.Document.SourceType))
            .OrderBy(g => Array.IndexOf(GroupOrder, g.Key))
            .Select(g => new ResearchContextGroupDto(
                g.Key,
                g.Select(h => new ResearchContextItemDto(
                    h.Document.Id,
                    h.Chunk.Id,
                    h.Document.SourceType.ToString(),
                    h.Document.SourceName,
                    h.Document.Title,
                    h.Document.CanonicalUrl,
                    h.Document.PublishedAt,
                    Snippet(h.Chunk.Text),
                    h.CombinedScore)).ToList()))
            .ToList();

        return new ResearchContextPacketDto(
            subjectType,
            query.ThesisId ?? thesis?.Id,
            ticker,
            thesis is null
                ? null
                : new ResearchContextThesisDto(
                    thesis.Id,
                    thesis.Ticker,
                    Truncate(thesis.ThesisText, ThesisSummaryLength),
                    thesis.BrokenAt is not null,
                    thesis.UpdatedAt,
                    thesis.NarrativeFalsifiers.ToList()),
            groups,
            Math.Max(0, result.TotalMatched - result.Hits.Count),
            DateTimeOffset.UtcNow);
    }

    private async Task<InvestmentThesis?> ResolveThesisAsync(GetResearchContextQuery query, CancellationToken ct)
    {
        if (query.ThesisId is not null)
        {
            return await theses.FindAsync(query.UserId, query.ThesisId.Value, ct);
        }

        if (string.IsNullOrWhiteSpace(query.Ticker))
        {
            return null;
        }

        var all = await theses.ListAsync(query.UserId, ct);
        return all
            .Where(t => t.Ticker.Equals(query.Ticker, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefault();
    }

    private static string GroupName(ResearchDocumentSourceType sourceType) => sourceType switch
    {
        ResearchDocumentSourceType.InvestmentThesis => "thesis",
        ResearchDocumentSourceType.DecisionNote => "decision_notes",
        ResearchDocumentSourceType.ThesisEvent => "decision_notes",
        ResearchDocumentSourceType.NewsArticle => "recent_news",
        ResearchDocumentSourceType.FilingExcerpt => "filings",
        ResearchDocumentSourceType.Postmortem => "postmortems",
        _ => "other_research",
    };

    private static string Snippet(string text)
        => text.Length <= SnippetLength ? text : $"{text[..SnippetLength].TrimEnd()}…";

    private static string Truncate(string text, int length)
        => text.Length <= length ? text : $"{text[..length].TrimEnd()}…";
}

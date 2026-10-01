namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.EntityFrameworkCore;

public class ResearchCorpusSourceReader(ResearchDbContext db) : IResearchCorpusSourceReader
{
    public async Task<IReadOnlyList<ResearchDocument>> LoadSourceDocumentsAsync(CancellationToken ct = default)
    {
        var documents = new List<ResearchDocument>();

        var articles = await db.News.AsNoTracking().ToListAsync(ct);
        foreach (var article in articles)
        {
            var text = string.IsNullOrWhiteSpace(article.Summary)
                ? article.Title
                : $"{article.Title}\n\n{article.Summary}";
            documents.Add(BuildDocument(
                ResearchDocumentSourceType.NewsArticle,
                article.Id.ToString(),
                userId: null,
                article.Title,
                text,
                article.Url,
                article.Source,
                article.PublishedAt,
                article.IngestedAt,
                article.Tickers,
                article.ThesisIds));
        }

        // The corpus spans every person; each owned document keeps its UserId, and retrieval filters on it.
        var theses = await db.Theses.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .ToListAsync(ct);
        foreach (var thesis in theses)
        {
            var text = thesis.BrokenAt is null
                ? thesis.ThesisText
                : $"{thesis.ThesisText}\n\nThesis marked broken {thesis.BrokenAt:yyyy-MM-dd}: {thesis.BrokenReason}";
            documents.Add(BuildDocument(
                ResearchDocumentSourceType.InvestmentThesis,
                thesis.Id.ToString(),
                thesis.UserId,
                $"{thesis.Ticker} investment thesis",
                text,
                canonicalUrl: null,
                sourceName: "Finance Sentry theses",
                thesis.UpdatedAt,
                thesis.UpdatedAt,
                [thesis.Ticker],
                [thesis.Id]));
        }

        var events = await db.ThesisEvents.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.DecisionNote != null && e.DecisionNote != string.Empty)
            .ToListAsync(ct);
        foreach (var thesisEvent in events)
        {
            documents.Add(BuildDocument(
                ResearchDocumentSourceType.DecisionNote,
                thesisEvent.Id.ToString(),
                thesisEvent.UserId,
                $"{thesisEvent.Ticker} {thesisEvent.EventType} decision note",
                thesisEvent.DecisionNote!,
                canonicalUrl: null,
                sourceName: "Finance Sentry decision journal",
                thesisEvent.Timestamp,
                thesisEvent.Timestamp,
                [thesisEvent.Ticker],
                thesisEvent.SubjectType == ThesisSubjectType.Thesis ? [thesisEvent.SubjectId] : []));
        }

        return documents;
    }

    private static ResearchDocument BuildDocument(
        ResearchDocumentSourceType sourceType,
        string sourceId,
        Guid? userId,
        string title,
        string text,
        string? canonicalUrl,
        string? sourceName,
        DateTimeOffset? publishedAt,
        DateTimeOffset capturedAt,
        List<string> tickers,
        List<Guid> thesisIds)
        => new()
        {
            SourceType = sourceType,
            SourceId = sourceId,
            UserId = userId,
            Title = title,
            CanonicalUrl = canonicalUrl,
            SourceName = sourceName,
            PublishedAt = publishedAt,
            CapturedAt = capturedAt,
            ContentHash = ResearchChunker.ComputeContentHash($"{title}\n{text}"),
            Text = text,
            Tickers = tickers.Select(t => t.ToUpperInvariant()).Distinct().ToList(),
            ThesisIds = thesisIds,
        };
}

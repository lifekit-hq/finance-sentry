namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Revisions;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Turns the evidence references on thesis revisions into labelled links for the owner's confirm card.</summary>
public interface IThesisEvidenceLinkResolver
{
    /// <summary>The links of each revision by its id; a revision without evidence has no entry.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ThesisEvidenceLinkDto>>> ResolveAsync(
        IReadOnlyCollection<ThesisRevision> revisions, CancellationToken ct = default);
}

public sealed class ThesisEvidenceLinkResolver(ResearchDbContext db) : IThesisEvidenceLinkResolver
{
    public const string NewsKind = "News";
    public const string FilingKind = "Filing";
    public const string EventKind = "Event";
    public const string FundamentalsKind = "Fundamentals";
    public const string MissingArticleLabel = "News article (no longer stored)";

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ThesisEvidenceLinkDto>>> ResolveAsync(
        IReadOnlyCollection<ThesisRevision> revisions, CancellationToken ct = default)
    {
        var articleIds = revisions
            .SelectMany(r => ParseIds(r.Evidence?.NewsArticleIds))
            .Distinct()
            .ToList();
        var articles = articleIds.Count == 0
            ? []
            : await db.News.AsNoTracking()
                .Where(a => articleIds.Contains(a.Id))
                .Select(a => new StoredArticle(a.Id, a.Title, a.Url))
                .ToDictionaryAsync(a => a.Id, ct);

        var result = new Dictionary<Guid, IReadOnlyList<ThesisEvidenceLinkDto>>();
        foreach (var revision in revisions)
        {
            if (revision.Evidence is not { } evidence)
            {
                continue;
            }

            var links = new List<ThesisEvidenceLinkDto>();
            foreach (var raw in evidence.NewsArticleIds ?? [])
            {
                links.Add(Guid.TryParse(raw, out var id) && articles.TryGetValue(id, out var article)
                    ? new ThesisEvidenceLinkDto(NewsKind, article.Title, SafeUrl(article.Url))
                    : new ThesisEvidenceLinkDto(NewsKind, MissingArticleLabel, null));
            }

            links.AddRange((evidence.FilingAccessions ?? []).Select(a => new ThesisEvidenceLinkDto(FilingKind, $"Filing {a}", null)));
            links.AddRange((evidence.EventIds ?? []).Select(e => new ThesisEvidenceLinkDto(EventKind, $"Event {e}", null)));
            if (!string.IsNullOrWhiteSpace(evidence.FundamentalsAsOf))
            {
                links.Add(new ThesisEvidenceLinkDto(FundamentalsKind, $"Fundamentals as of {evidence.FundamentalsAsOf}", null));
            }

            if (links.Count > 0)
            {
                result[revision.Id] = links;
            }
        }

        return result;
    }

    private sealed record StoredArticle(Guid Id, string Title, string Url);

    private static IEnumerable<Guid> ParseIds(IReadOnlyList<string>? raw)
        => (raw ?? []).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty);

    // Only web links reach the card: a stored javascript: or data: URL would become an href.
    private static string? SafeUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;
}

namespace FinanceSentry.Modules.Research.Domain.Dossier;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>The kinds of item a notable move links to.</summary>
public static class MoveLinkKind
{
    public const string News = "news";
    public const string Signal = "signal";
}

/// <summary>
/// One news item or Radar signal near a notable move. News carries <see cref="Url"/> (its source article);
/// a signal carries <see cref="Severity"/> and has no URL, it lives in the dossier's Radar section.
/// <see cref="Relevance"/> is the tie-break after time proximity (higher is better).
/// </summary>
public sealed record DossierMoveLink(
    string Kind,
    DateTimeOffset Timestamp,
    string Title,
    string Source,
    string? Url,
    string? Severity,
    int Relevance);

/// <summary>A day whose return reached the threshold, with the items found around it (empty = nothing found).</summary>
public sealed record DossierNotableMove(DateOnly Date, decimal ReturnPct, IReadOnlyList<DossierMoveLink> Links);

/// <summary>
/// The dossier's notable moves in the visible window, newest first. Empty <see cref="Moves"/> = no day reached
/// <see cref="ThresholdPct"/>.
/// </summary>
public sealed record DossierNotableMoves(
    decimal ThresholdPct, int WindowDays, int VisibleDays, IReadOnlyList<DossierNotableMove> Moves);

/// <summary>
/// Finds the notable moves in a close series and joins each to the news and Radar signals already ingested for the
/// asset. Pure: it invents no cause, an empty link list is the answer when nothing is near the move.
/// </summary>
public static class NotableMoveLinker
{
    private const int PercentScale = 100;
    private const int Decimals = 1;
    private const int TitleMentionRelevance = 2;
    private const int SoleTickerRelevance = 1;

    /// <summary>Moves on or after <paramref name="windowStart"/> whose absolute daily return is at least <paramref name="thresholdPct"/>.</summary>
    public static IReadOnlyList<(DateOnly Date, decimal ReturnPct)> FindMoves(
        IReadOnlyList<(DateOnly Date, decimal Close)> closes, DateOnly windowStart, decimal thresholdPct)
    {
        var moves = new List<(DateOnly, decimal)>();
        for (var i = 1; i < closes.Count; i++)
        {
            var previous = closes[i - 1].Close;
            if (previous <= 0m || closes[i].Date < windowStart)
            {
                continue;
            }

            var returnPct = Math.Round((closes[i].Close / previous - 1m) * PercentScale, Decimals);
            if (Math.Abs(returnPct) >= thresholdPct)
            {
                moves.Add((closes[i].Date, returnPct));
            }
        }

        return moves.OrderByDescending(m => m.Item1).ToList();
    }

    public static DossierNotableMoves Build(
        string symbol,
        IReadOnlyList<(DateOnly Date, decimal Close)> closes,
        IReadOnlyList<NewsArticleDto> news,
        IReadOnlyList<DossierSignalItem> signals,
        DateOnly windowStart,
        decimal thresholdPct,
        int windowDays,
        int visibleDays,
        int maxLinks)
    {
        var moves = FindMoves(closes, windowStart, thresholdPct)
            .Select(m => new DossierNotableMove(m.Date, m.ReturnPct, LinksFor(symbol, m.Date, news, signals, windowDays, maxLinks)))
            .ToList();
        return new DossierNotableMoves(thresholdPct, windowDays, visibleDays, moves);
    }

    private static IReadOnlyList<DossierMoveLink> LinksFor(
        string symbol, DateOnly day, IReadOnlyList<NewsArticleDto> news, IReadOnlyList<DossierSignalItem> signals,
        int windowDays, int maxLinks)
    {
        var candidates = news
            .Select(n => new DossierMoveLink(
                MoveLinkKind.News, n.PublishedAt, n.Title, n.Source, n.Url, null, NewsRelevance(symbol, n)))
            .Concat(signals.Select(s => new DossierMoveLink(
                MoveLinkKind.Signal, s.Timestamp, s.SignalType, s.Scanner, null, s.Severity, SignalRelevance(s.Severity))));

        // Time proximity first (whole days from the move's day), then relevance, then the newer item.
        return candidates
            .Select(c => (Link: c, Days: Math.Abs(DayOf(c.Timestamp).DayNumber - day.DayNumber)))
            .Where(c => c.Days <= windowDays)
            .OrderBy(c => c.Days)
            .ThenByDescending(c => c.Link.Relevance)
            .ThenByDescending(c => c.Link.Timestamp)
            .Take(Math.Max(0, maxLinks))
            .Select(c => c.Link)
            .ToList();
    }

    private static DateOnly DayOf(DateTimeOffset timestamp) => DateOnly.FromDateTime(timestamp.UtcDateTime);

    // An article that names the symbol in its title and tags only that symbol is about the asset, not a roundup.
    private static int NewsRelevance(string symbol, NewsArticleDto article)
        => (article.Title.Contains(symbol, StringComparison.OrdinalIgnoreCase) ? TitleMentionRelevance : 0)
           + (article.Tickers.Count == 1 ? SoleTickerRelevance : 0);

    private static int SignalRelevance(string severity) => severity.ToUpperInvariant() switch
    {
        "ALERTED" => 3,
        "NOTABLE" => 2,
        _ => 1,
    };
}

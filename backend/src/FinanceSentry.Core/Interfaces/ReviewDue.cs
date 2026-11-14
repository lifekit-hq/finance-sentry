namespace FinanceSentry.Core.Interfaces;

/// <summary>What a review-due notice is about: a thesis the owner holds, or an Active candidate (a watchlist name with no thesis yet).</summary>
public enum ReviewSubjectKind
{
    Thesis,
    Candidate,
}

/// <summary>
/// The six things that make a thesis or candidate worth a fresh look, as the strings carried on the alert and the
/// companion event. A hit is deterministic (a filing landed, a date passed, a trigger broke); what to conclude from it is
/// Ledger's job, never this repo's.
/// </summary>
public static class ReviewDueSource
{
    /// <summary>A periodic or current report landed on the subject's ticker or one of its trigger proxies.</summary>
    public const string Filing = "filing";

    /// <summary>The subject's earnings date has passed.</summary>
    public const string Earnings = "earnings";

    /// <summary>The subject's ticker clustered in the news.</summary>
    public const string News = "news";

    /// <summary>A dated catalyst on the thesis has passed.</summary>
    public const string Catalyst = "catalyst";

    /// <summary>The thesis monitor marked the thesis broken.</summary>
    public const string Break = "break";

    /// <summary>The thesis monitor cleared a break.</summary>
    public const string Unbreak = "unbreak";

    public static readonly IReadOnlyList<string> All = [Filing, Earnings, News, Catalyst, Break, Unbreak];

    /// <summary>
    /// True for the sources that go out at once (subject to the mode, quiet hours and the hourly cap): a filing, a break
    /// and an unbreak. News, earnings and catalyst ride the digest lane, which the three-times-a-day pulse reads.
    /// </summary>
    public static bool IsRealtime(string? source)
        => string.Equals(source, Filing, StringComparison.Ordinal)
            || string.Equals(source, Break, StringComparison.Ordinal)
            || string.Equals(source, Unbreak, StringComparison.Ordinal);
}

/// <summary>
/// One "look at this again" notice for a thesis or candidate. Raised once per (<paramref name="SubjectId"/>,
/// <paramref name="Source"/>, <paramref name="Reference"/>) - the reference names the thing that happened (an EDGAR accession
/// number, an earnings or catalyst date, a news day, a break timestamp), so re-detecting it never raises a second notice.
/// </summary>
/// <param name="Subject">Thesis or candidate.</param>
/// <param name="SubjectId">The thesis id or the candidate id.</param>
/// <param name="Ticker">The subject's own ticker (never a proxy's), which the notice opens on in the app.</param>
/// <param name="Source">One of <see cref="ReviewDueSource.All"/>.</param>
/// <param name="Reference">Stable key of the event behind the notice; part of the once-only identity.</param>
/// <param name="Evidence">Typed references Ledger can read the evidence from: <c>filing:&lt;accession&gt;</c>,
/// <c>url:&lt;document url&gt;</c>, <c>article:&lt;id&gt;</c>, <c>earnings:&lt;ticker&gt;:&lt;date&gt;</c>,
/// <c>catalyst:&lt;date&gt;</c>, <c>thesis:&lt;id&gt;</c>, <c>break:&lt;timestamp&gt;</c>.</param>
/// <param name="Headline">One human sentence for the alert text.</param>
/// <param name="NarrativeFalsifiers">A thesis subject's free-text kill-switches, handed to Ledger on every review because no
/// job monitors them. Null or empty for a candidate or a thesis without any.</param>
public sealed record ReviewDue(
    ReviewSubjectKind Subject,
    Guid SubjectId,
    string Ticker,
    string Source,
    string Reference,
    IReadOnlyList<string> Evidence,
    string Headline,
    IReadOnlyList<string>? NarrativeFalsifiers = null);

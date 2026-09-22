namespace FinanceSentry.Modules.Events.Domain;

/// <summary>
/// The kinds of dated, forward-looking events the calendar computes (feature 049). String constants
/// rather than an enum: they cross the REST and MCP boundaries verbatim, like <c>AlertType</c>.
/// </summary>
public static class EventKind
{
    public const string Earnings = "earnings";
    public const string ExDividend = "ex_dividend";
    public const string FilingDue = "filing_due";
    public const string Macro = "macro";
    public const string ThesisCatalyst = "thesis_catalyst";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Earnings, ExDividend, FilingDue, Macro, ThesisCatalyst,
    };
}

/// <summary>
/// The upcoming-event sources, named so a failed one can be reported on the result instead of
/// leaving a silently empty day. <c>Corporate</c> covers earnings and ex-dividend dates (one Yahoo
/// read); the other three map one-to-one to a kind.
/// </summary>
public static class EventSource
{
    public const string Corporate = "corporate";
    public const string Macro = "macro";
    public const string Theses = "theses";
    public const string Filings = "filings";
}

public static class EventSourceStatus
{
    public const string Ok = "ok";
    public const string Unavailable = "unavailable";
}

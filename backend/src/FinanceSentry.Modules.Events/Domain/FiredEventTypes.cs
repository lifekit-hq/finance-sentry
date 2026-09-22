namespace FinanceSentry.Modules.Events.Domain;

/// <summary>
/// The alert types the fired-events feed shows: exactly the five detector outputs the module
/// consumes (earnings-ahead, filing-landed, news-cluster, intraday-move = market structure,
/// budget-breach). Values are the cross-module <c>AlertType</c> strings; widening the feed is a
/// change to this list, nothing else.
/// </summary>
public static class FiredEventTypes
{
    public const string EarningsAhead = "EarningsAhead";
    public const string FilingLanded = "FilingLanded";
    public const string NewsCluster = "NewsCluster";
    public const string MarketStructure = "MarketStructure";
    public const string BudgetBreach = "BudgetBreach";

    public static readonly IReadOnlyList<string> All =
    [
        EarningsAhead, FilingLanded, NewsCluster, MarketStructure, BudgetBreach,
    ];
}

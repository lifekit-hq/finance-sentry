namespace FinanceSentry.Core.Headlines;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

/// <summary>
/// Today's scattered quality signals, gathered in one shape so <see cref="HeadlineQualityMapper"/> is the only
/// place that decides what they mean for a <see cref="Headline"/>.
/// </summary>
public sealed record HeadlineSignals
{
    public static HeadlineSignals None { get; } = new();

    /// <summary>Sources missing from the figure or carried forward from an earlier day (a stale sleeve, a failed read).</summary>
    public IReadOnlyList<string> StaleSources { get; init; } = [];

    /// <summary>The figure is a reconstruction rather than a measurement (a net-worth snapshot's <c>IsApproximate</c>).</summary>
    public bool BackfilledEstimate { get; init; }

    /// <summary>The worst conversion status among the amounts the figure was summed from.</summary>
    public FxStatus Fx { get; init; } = FxStatus.Exact;

    /// <summary>The figure is a cash or invested split that was not measured (a snapshot's <c>CashTotal</c> is null, or the split was withheld).</summary>
    public bool NoSplit { get; init; }

    /// <summary>No position the figure was summed from has a known cost basis, so the gain against cost is unavailable.</summary>
    public bool NoCostBasis { get; init; }

    /// <summary>
    /// Sources in the figure's scope whose external flows are not recorded (finance-framework §3.2 rule 4): a
    /// figure that splits value change from flows cannot be measured for them (<c>UNKNOWN_FLOW</c> per source).
    /// </summary>
    public IReadOnlyList<string> UnknownFlowSources { get; init; } = [];

    /// <summary>A source joined the scope inside the window and the measure bridged it (<c>SCOPE_CHANGED</c>, informative).</summary>
    public bool ScopeChanged { get; init; }

    /// <summary>
    /// Sources with a transfer leg inside the window that no leg on another source in scope matched, read as an
    /// external flow (finance-framework §3.2 rule 6): the figure is an estimate (<c>UNMATCHED_TRANSFER</c> per source).
    /// </summary>
    public IReadOnlyList<string> UnmatchedTransferSources { get; init; } = [];

    /// <summary>
    /// Sources whose value in the figure is an estimate rather than a quoted price (reliability L3, framework §5.3
    /// step 3): the figure is ESTIMATED (<c>ESTIMATED_VALUE</c> per source).
    /// </summary>
    public IReadOnlyList<string> EstimatedSources { get; init; } = [];

    /// <summary>Both figures' signals: the start and the end of a window.</summary>
    public HeadlineSignals Merge(HeadlineSignals other) => new()
    {
        StaleSources = [.. StaleSources.Concat(other.StaleSources).Distinct(StringComparer.OrdinalIgnoreCase)],
        BackfilledEstimate = BackfilledEstimate || other.BackfilledEstimate,
        Fx = Fx.Worst(other.Fx),
        NoSplit = NoSplit || other.NoSplit,
        NoCostBasis = NoCostBasis || other.NoCostBasis,
        UnknownFlowSources = [.. UnknownFlowSources.Concat(other.UnknownFlowSources).Distinct(StringComparer.OrdinalIgnoreCase)],
        ScopeChanged = ScopeChanged || other.ScopeChanged,
        UnmatchedTransferSources = [.. UnmatchedTransferSources.Concat(other.UnmatchedTransferSources).Distinct(StringComparer.OrdinalIgnoreCase)],
        EstimatedSources = [.. EstimatedSources.Concat(other.EstimatedSources).Distinct(StringComparer.OrdinalIgnoreCase)],
    };

    /// <summary>
    /// A stored net-worth snapshot's flags: <paramref name="staleSleeves"/> is its comma-separated
    /// <c>StaleSleeves</c>; a row with no recorded <paramref name="recordedFx"/> is valued at today's rate when it
    /// is a reconstruction and exactly otherwise (the same rule the history endpoint applies).
    /// </summary>
    public static HeadlineSignals FromSnapshot(string? staleSleeves, bool isApproximate, FxStatus? recordedFx) => new()
    {
        StaleSources = SplitSources(staleSleeves),
        BackfilledEstimate = isApproximate,
        Fx = recordedFx ?? (isApproximate ? FxStatus.TodayRate : FxStatus.Exact),
    };

    /// <summary>
    /// A net-worth history row as the history query returns it: its <paramref name="fxReasons"/> are the reason
    /// codes it already resolved from the stored status (<see cref="FxStatusExtensions.ToReasons"/>).
    /// </summary>
    public static HeadlineSignals FromSnapshot(string? staleSleeves, bool isApproximate, IReadOnlyList<string>? fxReasons) => new()
    {
        StaleSources = SplitSources(staleSleeves),
        BackfilledEstimate = isApproximate,
        Fx = fxReasons is null ? FxStatus.Exact
            : fxReasons.Contains(FxReasons.Unknown1To1) ? FxStatus.Unknown1To1
            : fxReasons.Contains(FxReasons.TodayRate) ? FxStatus.TodayRate
            : FxStatus.Exact,
    };

    /// <summary>
    /// The canonical book figures' flags: the sources that failed to read, the worst conversion, and the sources
    /// that read an estimated (L3) value into the total.
    /// </summary>
    public static HeadlineSignals FromBook(BookFigures book) => new()
    {
        StaleSources = book.StaleSources,
        Fx = book.FxStatus,
        EstimatedSources = EstimatedIn(book, r => r.TotalValueUsd),
    };

    /// <summary>
    /// The book's sources that read an estimated (L3) value into the part of their reading <paramref name="part"/>
    /// selects (the total, the cash, the invested value): a figure without that part is not an estimate.
    /// </summary>
    public static IReadOnlyList<string> EstimatedIn(BookFigures book, Func<WealthSourceReading, decimal> part) =>
        [.. book.Sources
            .Where(s => s.Reading is { Reliability: Reliability.L3Estimated } r && part(r) != 0m)
            .Select(s => s.SourceId)];

    private static IReadOnlyList<string> SplitSources(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

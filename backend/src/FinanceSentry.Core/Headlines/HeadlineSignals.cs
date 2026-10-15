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

    /// <summary>Both figures' signals: the start and the end of a window.</summary>
    public HeadlineSignals Merge(HeadlineSignals other) => new()
    {
        StaleSources = [.. StaleSources.Concat(other.StaleSources).Distinct(StringComparer.OrdinalIgnoreCase)],
        BackfilledEstimate = BackfilledEstimate || other.BackfilledEstimate,
        Fx = Fx.Worst(other.Fx),
        NoSplit = NoSplit || other.NoSplit,
        NoCostBasis = NoCostBasis || other.NoCostBasis,
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

    /// <summary>The canonical book figures' flags: the sources that failed to read and the worst conversion.</summary>
    public static HeadlineSignals FromBook(BookFigures book) => new()
    {
        StaleSources = book.StaleSources,
        Fx = book.FxStatus,
    };

    private static IReadOnlyList<string> SplitSources(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

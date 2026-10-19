namespace FinanceSentry.Core.Headlines;

using System.Text.Json.Serialization;

/// <summary>
/// The typed record every top-line figure carries (finance-framework §3.6): what the number is, how it was
/// measured, over what, and how far to trust it. Responses carry it next to their existing fields, so a reader
/// that only knows the old fields keeps working. Built through <see cref="HeadlineFactory"/>, which routes every
/// signal through <see cref="HeadlineQualityMapper"/>.
/// </summary>
/// <param name="Amount">The figure in <paramref name="Currency"/>; null when it is unavailable, never 0 in its place.</param>
/// <param name="Percent">
/// Percentage points (5.0 = 5%) of <paramref name="Basis"/>, sharing the numerator of <paramref name="Amount"/>.
/// Null unless the method has one and the basis supports it (a zero or missing basis, a scope change): the reason says why.
/// Never annualised.
/// </param>
/// <param name="Method">How the figure was measured.</param>
/// <param name="Basis">What <paramref name="Percent"/> is a percent of; null for a point-in-time <see cref="HeadlineMethod.Value"/>.</param>
/// <param name="Scope">What the figure covers (<see cref="HeadlineScope"/>).</param>
/// <param name="Window">The dates the figure spans; a point-in-time value has only <see cref="HeadlineWindow.To"/>.</param>
/// <param name="Quality">How far to trust the figure; the worst of its reasons.</param>
/// <param name="BasisStatus">Whether the cost basis or start value behind <paramref name="Percent"/> is complete; null when the method has no basis.</param>
/// <param name="Coverage">The share (0..1) of the scope's value the figure measures, for a figure measured over part of its scope; null otherwise.</param>
/// <param name="Reasons">Typed reason codes, never free text; empty for a complete figure.</param>
/// <param name="AsOf">The oldest input timestamp behind the figure, when the sources record one.</param>
/// <param name="Currency">The currency <paramref name="Amount"/> is expressed in.</param>
public sealed record Headline(
    decimal? Amount,
    decimal? Percent,
    HeadlineMethod Method,
    HeadlineBasis? Basis,
    string Scope,
    HeadlineWindow Window,
    HeadlineQuality Quality,
    BasisStatus? BasisStatus,
    decimal? Coverage,
    IReadOnlyList<HeadlineReason> Reasons,
    DateTimeOffset? AsOf,
    string Currency);

/// <summary>The dates a figure spans. <see cref="From"/> is null for a point-in-time value.</summary>
public sealed record HeadlineWindow(DateOnly? From, DateOnly To);

/// <summary>One typed reason. <see cref="Source"/> names the source a per-source reason is about (a stale sleeve).</summary>
public sealed record HeadlineReason(HeadlineReasonCode Code, string? Source = null);

/// <summary>How a figure was measured (finance-framework §3.4 measures).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HeadlineMethod>))]
public enum HeadlineMethod
{
    /// <summary>A point-in-time value (net worth, a sleeve's value): no window, no percent.</summary>
    [JsonStringEnumMemberName("VALUE")]
    Value,

    /// <summary>M1: end minus start, over the start value; includes deposits and withdrawals.</summary>
    [JsonStringEnumMemberName("VALUE_CHANGE")]
    ValueChange,

    /// <summary>M2: the change split into contributions and growth.</summary>
    [JsonStringEnumMemberName("CONTRIBUTIONS_VS_GROWTH")]
    ContributionsVsGrowth,

    /// <summary>M3: time-weighted return.</summary>
    [JsonStringEnumMemberName("TWR")]
    Twr,

    /// <summary>M4: money-weighted return (XIRR).</summary>
    [JsonStringEnumMemberName("MWR_XIRR")]
    MwrXirr,

    /// <summary>Modified Dietz approximation of a money-weighted return.</summary>
    [JsonStringEnumMemberName("MODIFIED_DIETZ")]
    ModifiedDietz,

    /// <summary>M6: gain against cost basis.</summary>
    [JsonStringEnumMemberName("GAIN_VS_COST")]
    GainVsCost,

    /// <summary>Holding-period return of a single position.</summary>
    [JsonStringEnumMemberName("HPR")]
    Hpr,
}

/// <summary>What a percent is a percent of.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HeadlineBasis>))]
public enum HeadlineBasis
{
    [JsonStringEnumMemberName("START_VALUE")]
    StartValue,

    [JsonStringEnumMemberName("COST")]
    Cost,

    [JsonStringEnumMemberName("NET_CONTRIBUTED")]
    NetContributed,

    [JsonStringEnumMemberName("AVERAGE_CAPITAL")]
    AverageCapital,
}

/// <summary>How far to trust a figure, ordered from best to worst so the worst is the maximum.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HeadlineQuality>))]
public enum HeadlineQuality
{
    /// <summary>Every input measured, every rate live.</summary>
    [JsonStringEnumMemberName("COMPLETE")]
    Complete = 0,

    /// <summary>An input stands in for the real one (a reconstructed day, today's rate for a past date).</summary>
    [JsonStringEnumMemberName("ESTIMATED")]
    Estimated = 1,

    /// <summary>An input is missing or wrong (a stale source, a currency passed through 1:1).</summary>
    [JsonStringEnumMemberName("DEGRADED")]
    Degraded = 2,

    /// <summary>There is no figure: <see cref="Headline.Amount"/> is null.</summary>
    [JsonStringEnumMemberName("UNAVAILABLE")]
    Unavailable = 3,
}

/// <summary>Whether the cost basis or start value behind a percent covers the whole scope.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BasisStatus>))]
public enum BasisStatus
{
    [JsonStringEnumMemberName("COMPLETE")]
    Complete,

    [JsonStringEnumMemberName("PARTIAL")]
    Partial,

    [JsonStringEnumMemberName("MISSING")]
    Missing,
}

/// <summary>
/// Why a figure is less than complete, or why it has no percent. Typed codes: the frontend turns them into words
/// through its message registry, never by parsing text. Codes no source raises yet are listed so every reader
/// shares one vocabulary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HeadlineReasonCode>))]
public enum HeadlineReasonCode
{
    /// <summary>A source could not be read or was carried forward from an earlier day; <see cref="HeadlineReason.Source"/> names it.</summary>
    [JsonStringEnumMemberName("STALE_SOURCE")]
    StaleSource,

    /// <summary>The figure is a reconstruction, not a measurement (a backfilled net-worth day).</summary>
    [JsonStringEnumMemberName("BACKFILLED_ESTIMATE")]
    BackfilledEstimate,

    /// <summary>A rate other than the valuation date's own was used (today's rate for a past date, the offline seed table).</summary>
    [JsonStringEnumMemberName("FX_TODAY_RATE")]
    FxTodayRate,

    /// <summary>A currency with no rate passed through 1:1.</summary>
    [JsonStringEnumMemberName("FX_UNKNOWN_1TO1")]
    FxUnknown1To1,

    /// <summary>The cash/invested split was not measured, so the split figure is unavailable.</summary>
    [JsonStringEnumMemberName("NO_SPLIT")]
    NoSplit,

    /// <summary>Accounts joined or left the scope inside the window, so the change has no percent.</summary>
    [JsonStringEnumMemberName("SCOPE_CHANGED")]
    ScopeChanged,

    /// <summary>The history does not reach back far enough to measure the window.</summary>
    [JsonStringEnumMemberName("SHORT_HISTORY")]
    ShortHistory,

    /// <summary>The basis is zero, so there is no percent.</summary>
    [JsonStringEnumMemberName("ZERO_BASE")]
    ZeroBase,

    /// <summary>The basis is negative (a margin deficit), so a percent of it has no meaning.</summary>
    [JsonStringEnumMemberName("NEGATIVE_BASE")]
    NegativeBase,

    /// <summary>The measure could not produce a figure from its inputs for a reason not listed above.</summary>
    [JsonStringEnumMemberName("NOT_MEASURABLE")]
    NotMeasurable,

    /// <summary>A flow whose class is unknown sits inside the window.</summary>
    [JsonStringEnumMemberName("UNKNOWN_FLOW")]
    UnknownFlow,

    /// <summary>A transfer leg has no matching leg on the other side.</summary>
    [JsonStringEnumMemberName("UNMATCHED_TRANSFER")]
    UnmatchedTransfer,

    /// <summary>A non-cash item is counted in a cash figure.</summary>
    [JsonStringEnumMemberName("NON_CASH_INCLUDED")]
    NonCashIncluded,

    /// <summary>A price was carried forward from an earlier day.</summary>
    [JsonStringEnumMemberName("PRICE_CARRIED_FORWARD")]
    PriceCarriedForward,

    /// <summary>The net-worth bridge leaves a residual above its tolerance.</summary>
    [JsonStringEnumMemberName("RESIDUAL_ABOVE_TOLERANCE")]
    ResidualAboveTolerance,

    /// <summary>No position in the scope has a known cost basis, so a gain against cost cannot be measured.</summary>
    [JsonStringEnumMemberName("NO_COST_BASIS")]
    NoCostBasis,

    /// <summary>A value in the figure is an estimate, not a quoted price (reliability L3: one the person entered); <see cref="HeadlineReason.Source"/> names its source.</summary>
    [JsonStringEnumMemberName("ESTIMATED_VALUE")]
    EstimatedValue,
}

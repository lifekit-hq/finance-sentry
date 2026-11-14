namespace FinanceSentry.Modules.PortfolioConstruction.API.Responses;

/// <summary>
/// Gap mode (spec 726 US4): the book the viewer holds against the target the constructor builds for an archetype,
/// as "missing X, overweight Y" with amounts and the sector each name belongs to. Everything is a difference between
/// two figures that are both shown, in percent of the whole book and in USD; nothing is scored, and nothing here
/// is a recommendation to trade or prepares an order (<see cref="Label"/> says so on every response). Percentages
/// are rounded to 0.01 point and USD to the cent, so a column may differ from its total by a rounding residual.
/// </summary>
/// <param name="Label">The informational label the screen and the MCP tool must show with the figures.</param>
/// <param name="HasBook">False when the book has no value to size against: percentages only, every USD figure is null.</param>
/// <param name="BookValueUsd">The denominator of every percentage: the canonical book total (cash included).</param>
/// <param name="Refusal">Set when the policy cannot be built as written; no gap is given then.</param>
public sealed record GapModeResult(
    string Archetype,
    DateTimeOffset GeneratedAt,
    string Label,
    bool HasBook,
    decimal BookValueUsd,
    bool BookStale,
    IReadOnlyList<string> StaleSources,
    string? Refusal,
    GapSummaryDto Summary,
    IReadOnlyList<SleeveGapDto> Sleeves,
    IReadOnlyList<SectorGapDto> Sectors,
    IReadOnlyList<NameGapDto> Names,
    IReadOnlyList<string> Notes);

/// <summary>The one-line reading of the gap and the counts behind it. Names are ordered by the size of their gap.</summary>
public sealed record GapSummaryDto(
    string Headline,
    IReadOnlyList<string> MissingNames,
    IReadOnlyList<string> UnderweightNames,
    IReadOnlyList<string> OverweightNames,
    int OnTargetCount);

/// <summary>
/// One asset-class sleeve: held against the constructed target. <c>GapPct</c> is target minus current, so a positive
/// gap is a shortfall and a negative one is an excess; <c>GapUsd</c> is the same difference in dollars.
/// </summary>
public sealed record SleeveGapDto(
    string AssetClass,
    string Status,
    decimal CurrentPct,
    decimal TargetPct,
    decimal GapPct,
    decimal CurrentUsd,
    decimal? TargetUsd,
    decimal? GapUsd);

/// <summary>
/// One sector of the equities sleeve: the names that fall in it, held against target. A name with no sector from
/// the source sits under <c>Unclassified</c>; the sector is a profile fact and never an input to the target.
/// </summary>
public sealed record SectorGapDto(
    string Sector,
    string Status,
    decimal CurrentPct,
    decimal TargetPct,
    decimal GapPct,
    decimal CurrentUsd,
    decimal? TargetUsd,
    decimal? GapUsd,
    IReadOnlyList<string> Symbols);

/// <summary>
/// One equity name, held against its constructed weight. <c>Status</c> is Missing (in the target, not held),
/// Underweight, OnTarget or Overweight; <c>HeldOutsideTarget</c> marks an Overweight name the target gives no weight
/// at all. <c>Verdict</c> is the name's verdict word (enum name, null when it was not scored): the gap line states
/// the position, the verdict is the call. <c>Reason</c> is the construction's own line for a target name, or why a
/// held name has no weight.
/// </summary>
public sealed record NameGapDto(
    string Symbol,
    string? Sector,
    string? Verdict,
    string Status,
    bool HeldOutsideTarget,
    decimal CurrentPct,
    decimal TargetPct,
    decimal GapPct,
    decimal CurrentUsd,
    decimal? TargetUsd,
    decimal? GapUsd,
    string Reason);

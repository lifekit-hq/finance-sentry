namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Dossier;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Positions;

/// <summary>
/// Aggregate per-ticker dossier returned by GET /research/assets/{symbol}/dossier (feature 421).
/// Each section is null / empty when no data is available for that type — never a 500.
/// Position: null = not a holding. Thesis: null = no thesis. Valuation: null = no data.
/// Analysts: null = no coverage. NextEarnings: null = none in the next 90 days.
/// Performance: the one-year strip, null = no price history. Checks: the summary wheel's traceable
/// rules (always present; an axis that does not apply is absent). UserTarget: null = none set.
/// PendingRecommendation: Ledger's pending proposal on the name (#1069), null = none.
/// Scorecard: the four-axis scorecard and its verdict word (spec 726 US2), null = it could not be read.
/// </summary>
public sealed record AssetDossierResult(
    string Symbol,
    DossierPositionSection? Position,
    ThesisDto? Thesis,
    ValuationSnapshotResult? Valuation,
    DossierAnalystsSection? Analysts,
    IReadOnlyList<NewsArticleDto> RecentNews,
    EarningsEventDto? NextEarnings,
    IReadOnlyList<DossierSignalItem> RadarSignals,
    DateTimeOffset GeneratedAt,
    DossierPerformance? Performance = null,
    DossierChecks? Checks = null,
    DossierUserTarget? UserTarget = null,
    RecommendationDto? PendingRecommendation = null,
    ScorecardResult? Scorecard = null);

/// <summary>The user's own fair-value target, in <see cref="Currency"/> (the quote currency it was set in).</summary>
public sealed record DossierUserTarget(decimal TargetPrice, string? Currency, DateTimeOffset UpdatedAt);

/// <summary>
/// Position and tax-lot detail for a holding in the user's book. TaxLots: IBKR only; empty for crypto.
/// AssetClass: the canonical bucket from <see cref="FinanceSentry.Core.Domain.AssetClassNormalizer"/>.
/// ShareOfBookPercent: the position's share of the book's total value (cash included, the same
/// denominator as the holdings weights); null when the book total is not positive.
/// PositionCapPercent: the IPS single-position cap in percent; null when unset.
/// Returns: the return decomposition from the ingested fills and dividends; null when it could not be read.
/// </summary>
public sealed record DossierPositionSection(
    string Provider,
    string AssetClass,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    IReadOnlyList<DossierTaxLotEntry> TaxLots,
    decimal? ShareOfBookPercent = null,
    decimal? PositionCapPercent = null,
    PositionReturns? Returns = null);

/// <summary>Analyst actions + consensus trend for the ticker.</summary>
public sealed record DossierAnalystsSection(
    IReadOnlyList<AnalystActionDto> RecentActions,
    IReadOnlyList<RecommendationTrendDto> Trends,
    string Coverage);

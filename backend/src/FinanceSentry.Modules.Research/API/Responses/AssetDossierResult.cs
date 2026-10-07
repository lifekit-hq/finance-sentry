namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Aggregate per-ticker dossier returned by GET /research/assets/{symbol}/dossier (feature 421).
/// Each section is null / empty when no data is available for that type — never a 500.
/// Position: null = not a holding. Thesis: null = no thesis. Valuation: null = no data.
/// Analysts: null = no coverage. NextEarnings: null = none in the next 90 days.
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
    DateTimeOffset GeneratedAt);

/// <summary>
/// Position and tax-lot detail for a holding in the user's book. TaxLots: IBKR only; empty for crypto.
/// AssetClass: the canonical bucket from <see cref="FinanceSentry.Core.Domain.AssetClassNormalizer"/>.
/// </summary>
public sealed record DossierPositionSection(
    string Provider,
    string AssetClass,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    IReadOnlyList<DossierTaxLotEntry> TaxLots);

/// <summary>Analyst actions + consensus trend for the ticker.</summary>
public sealed record DossierAnalystsSection(
    IReadOnlyList<AnalystActionDto> RecentActions,
    IReadOnlyList<RecommendationTrendDto> Trends,
    string Coverage);

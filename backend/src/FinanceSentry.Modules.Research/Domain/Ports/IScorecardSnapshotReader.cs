namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (spec 726): one symbol's scorecard and verdict, flattened for cross-module readers such as
/// the Portfolio Construction module's universe screen. Implemented inside Research over the same
/// <c>GetScorecardQuery</c> the dossier uses, so a reader can never disagree with the dossier's verdict; the
/// FinanceSentry.Integration adapter reaches Research only through this interface. Shared corpus: nothing in it is
/// per-user. There is no composite, weighted or overall number on it (019 FR-007 as amended).
/// </summary>
public interface IScorecardSnapshotReader
{
    Task<ScorecardSnapshot> GetAsync(string symbol, CancellationToken ct = default);
}

/// <summary>
/// The four axes as facts and the verdict word with the rule that fired. Enums cross as their names
/// (<see cref="VerdictWord"/> is <c>StrongBuy</c>, <c>Buy</c>, <c>Wait</c>, <c>Avoid</c> or <c>NotScored</c>).
/// </summary>
public sealed record ScorecardSnapshot(
    string Symbol,
    DateTimeOffset GeneratedAt,
    int? StructureRaw,
    int? StructureAdjusted,
    string StructureBasis,
    IReadOnlyList<string> StructureNotEvaluable,
    int? Fundamentals,
    IReadOnlyList<string> FundamentalsNotEvaluable,
    string? Crowding,
    decimal? ExtensionFromMa50,
    bool ValuationNotApplicable,
    decimal? TrailingPe,
    decimal? TrailingPeFiveYearAvg,
    decimal? ValuationPremiumPct,
    decimal? ImpliedUpsidePct,
    string? ValuationNotEvaluable,
    bool ValuationStale,
    string VerdictWord,
    int RulesVersion,
    string FiredRuleId,
    string? FiredRuleDescription);

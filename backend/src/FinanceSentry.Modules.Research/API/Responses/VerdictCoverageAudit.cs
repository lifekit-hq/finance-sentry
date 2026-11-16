namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>
/// The verdict-coverage audit (#1413, plan #1273): one row per held, thesis, watched or coverage symbol, with the state of
/// every axis and section a verdict reads and, where a section has no usable data, a gap reason from the closed
/// <see cref="VerdictCoverageGap"/>. Computed at request time from the owner's own readers and persisted nowhere. It is
/// a read of what the verdict could see, not a recommendation: nothing here acts on a price or a word.
/// <see cref="SymbolCount"/> is the universe size before the cap; <see cref="Truncated"/> says rows were left out.
/// </summary>
public sealed record VerdictCoverageAudit(
    DateTimeOffset GeneratedAt,
    int SymbolCount,
    bool Truncated,
    IReadOnlyList<VerdictCoverageRow> Rows);

/// <summary>
/// One symbol of the audit. <see cref="Origins"/> are the lists it came from (<see cref="VerdictCoverageOrigins"/>).
/// <see cref="Universe"/> is whether the Radar universe takes it. Each other section is a <see cref="CoverageSection"/>.
/// </summary>
public sealed record VerdictCoverageRow(
    string Symbol,
    IReadOnlyList<string> Origins,
    CoverageBookFacts Book,
    CoverageSection Universe,
    CoverageSection Structure,
    CoverageSection Fundamentals,
    CoverageSection Valuation,
    CoverageSection Peers,
    CoverageSection LookThrough,
    CoverageVerdict Verdict);

/// <summary>What the holdings feed knows about the symbol; <see cref="Gap"/> is <see cref="VerdictCoverageGap.SourceMissing"/> when it has no brokerage row.</summary>
public sealed record CoverageBookFacts(string? Provider, string? InstrumentType, string? AssetClass, VerdictCoverageGap? Gap);

/// <summary>One section's state: <see cref="Gap"/> null means read; <see cref="Detail"/> is a short fact (a lens, a count, an exception type).</summary>
public sealed record CoverageSection(VerdictCoverageGap? Gap, string? Detail = null);

/// <summary>The verdict the scorecard read gave, or <see cref="VerdictCoverageGap.ScorecardThrew"/> with the exception type in <see cref="Detail"/>.</summary>
public sealed record CoverageVerdict(
    string? Word,
    string? FiredRuleId,
    string? WaitReason,
    VerdictCoverageGap? Gap,
    string? Detail = null);

/// <summary>The lists a symbol of the audit came from.</summary>
public static class VerdictCoverageOrigins
{
    public const string Held = "held";
    public const string Thesis = "thesis";
    public const string Watched = "watched";
    public const string Coverage = "coverage";
}

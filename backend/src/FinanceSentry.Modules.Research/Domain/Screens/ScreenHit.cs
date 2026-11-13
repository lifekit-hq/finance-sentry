namespace FinanceSentry.Modules.Research.Domain.Screens;

/// <summary>
/// The stable names a screen's nominations carry. A screen nominates a <c>Scan</c> candidate whose FIRST nomination
/// reason is the screen's tag: that is what the Ledger scoreboard reads to attribute a coverage add to the screen that
/// found it (<c>CoverageSourceGradeDto.ScreenTag</c>), so the tags are fixed strings, never built from figures (a
/// figure would make every night a new reason and fork the grading groups).
/// </summary>
public static class ScreenTags
{
    public const string Prefix = "screen: ";

    public const string Fundamentals = Prefix + "fundamentals";
    public const string RelativeStrength = Prefix + "relative strength";
    public const string Catalyst = Prefix + "dated catalyst";

    public static readonly IReadOnlyList<string> All = [Fundamentals, RelativeStrength, Catalyst];

    public static bool IsScreenTag(string reason) => reason.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>
/// One name a screen found. <see cref="Tag"/> leads the candidate's nomination reasons, <see cref="SupportingReasons"/>
/// follow it (the rule reasons the existing scan rules gave), and <see cref="Detail"/> is the facts in words for the
/// candidate's creation note. <see cref="EvidenceAt"/> is when the evidence the hit rests on became available: a name
/// the user's coverage dropped is nominated again only on evidence later than the removal (<see cref="ScreenSuppression"/>).
/// </summary>
public sealed record ScreenHit(
    string Tag,
    string Ticker,
    IReadOnlyList<string> SupportingReasons,
    string Detail,
    DateTimeOffset EvidenceAt)
{
    /// <summary>The nomination reasons in order: the tag first, then the supporting rule reasons without a repeat.</summary>
    public IReadOnlyList<string> Reasons =>
        [Tag, .. SupportingReasons.Where(r => !string.Equals(r, Tag, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
}

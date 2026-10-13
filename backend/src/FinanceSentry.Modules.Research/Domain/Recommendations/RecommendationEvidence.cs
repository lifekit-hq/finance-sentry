namespace FinanceSentry.Modules.Research.Domain.Recommendations;

/// <summary>
/// One piece of evidence behind a proposal: a <see cref="RecommendationEvidenceKind"/>, a human label with the number
/// it rests on, and the id of the row it points at when there is one.
/// </summary>
public sealed record RecommendationEvidence(string Kind, string Label, string? RefId = null)
{
    public const int MaxLabelLength = 300;
    public const int MaxRefIdLength = 120;
    public const int MaxCount = 12;
}

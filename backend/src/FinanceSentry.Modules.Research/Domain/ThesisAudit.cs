namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The audit payload of a <see cref="ThesisEvent"/> (jsonb): the thesis as it stood before and after the write,
/// why it was made and what it rested on. A revision is reversible from <see cref="Prior"/>.
/// </summary>
public sealed record ThesisAudit(
    ThesisVersion? Prior,
    ThesisVersion? New,
    string? Rationale,
    ThesisEvidenceRefs? Evidence,
    Guid? RevertsEventId = null);

/// <summary>The restorable content of an <see cref="InvestmentThesis"/> (everything a revision can change).</summary>
public sealed record ThesisVersion(
    string Ticker,
    string ThesisText,
    decimal? EntryPrice,
    IReadOnlyList<ThesisDataPoint> KeyDataPoints,
    IReadOnlyList<ThesisCatalyst> Catalysts,
    IReadOnlyList<ThesisInvalidationTrigger> InvalidationTriggers)
{
    public static ThesisVersion Of(InvestmentThesis thesis) => new(
        thesis.Ticker,
        thesis.ThesisText,
        thesis.EntryPrice,
        thesis.KeyDataPoints.ToList(),
        thesis.Catalysts.ToList(),
        thesis.InvalidationTriggers.ToList());

    /// <summary>Structural equality (the lists compare by element), which the record's own equality does not give.</summary>
    public bool SameContentAs(ThesisVersion other) =>
        Ticker == other.Ticker
        && ThesisText == other.ThesisText
        && EntryPrice == other.EntryPrice
        && KeyDataPoints.SequenceEqual(other.KeyDataPoints)
        && Catalysts.SequenceEqual(other.Catalysts)
        && InvalidationTriggers.SequenceEqual(other.InvalidationTriggers);

    public void ApplyTo(InvestmentThesis thesis)
    {
        thesis.Ticker = Ticker;
        thesis.ThesisText = ThesisText;
        thesis.EntryPrice = EntryPrice;
        thesis.KeyDataPoints = KeyDataPoints.ToList();
        thesis.Catalysts = Catalysts.ToList();
        thesis.InvalidationTriggers = InvalidationTriggers.ToList();
    }
}

/// <summary>What a write rested on: references into the stored research, so the audit can be checked against it.</summary>
public sealed record ThesisEvidenceRefs(
    IReadOnlyList<string>? NewsArticleIds = null,
    IReadOnlyList<string>? FilingAccessions = null,
    IReadOnlyList<string>? EventIds = null,
    string? FundamentalsAsOf = null);

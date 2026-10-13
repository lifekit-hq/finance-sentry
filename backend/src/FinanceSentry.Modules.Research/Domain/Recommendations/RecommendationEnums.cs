namespace FinanceSentry.Modules.Research.Domain.Recommendations;

/// <summary>What the proposal asks the owner to do. <see cref="Hold"/> is only ever an answer to a fired event.</summary>
public enum RecommendationAction
{
    Add,
    Hold,
    Trim,
    Exit,
}

public enum RecommendationStatus
{
    Pending,
    Confirmed,
    Rejected,
    Expired,
    Superseded,
}

/// <summary>Who drafted it: Ledger through its MCP tools, or the server's rule sweep. Both are shown as Ledger's.</summary>
public enum RecommendationOrigin
{
    Ledger,
    Rules,
}

/// <summary>The rule-anchored reasons a proposal may exist; anything else stays silent.</summary>
public enum RecommendationTriggerKind
{
    /// <summary>A thesis on the ticker broke.</summary>
    ThesisBreak,

    /// <summary>The ticker's asset-class sleeve is outside its IPS band.</summary>
    IpsBand,

    /// <summary>A risk rule is breached (the single-position cap).</summary>
    RiskRule,

    /// <summary>A recent Radar signal on the ticker.</summary>
    RadarSignal,

    /// <summary>A fired event Ledger recorded a verdict on - the only trigger a Hold may answer.</summary>
    EventVerdict,
}

public enum RecommendationConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>Whether a confirmed proposal was placed: a matching fill within the window after the decision.</summary>
public enum RecommendationExecution
{
    Pending,
    Executed,
    Unexecuted,
    NotApplicable,
}

/// <summary>Where an evidence reference points.</summary>
public static class RecommendationEvidenceKind
{
    public const string Thesis = "Thesis";
    public const string RiskRule = "RiskRule";
    public const string PolicyReview = "PolicyReview";
    public const string Sleeve = "Sleeve";
    public const string RadarSignal = "RadarSignal";
    public const string Event = "Event";
    public const string Candidate = "Candidate";
    public const string Note = "Note";

    public static readonly IReadOnlyList<string> All =
        [Thesis, RiskRule, PolicyReview, Sleeve, RadarSignal, Event, Candidate, Note];
}

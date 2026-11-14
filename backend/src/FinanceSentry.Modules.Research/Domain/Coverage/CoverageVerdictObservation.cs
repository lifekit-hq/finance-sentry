namespace FinanceSentry.Modules.Research.Domain.Coverage;

using FinanceSentry.Core.Auth;

/// <summary>
/// One day's verdict word for a candidate ticker, recorded by the coverage evaluation so "scored Buy twice within 14
/// days" and "two consecutive Avoid verdicts" are decided from a stored history, not from a re-read of the market.
/// One row per user, ticker and UTC day.
/// </summary>
public class CoverageVerdictObservation : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Ticker { get; set; } = string.Empty;

    /// <summary>The UTC day the verdict was read.</summary>
    public DateOnly ObservedOn { get; set; }

    /// <summary>The verdict word (<c>StrongBuy</c>, <c>Buy</c>, <c>Wait</c>, <c>Avoid</c>, <c>NotScored</c>).</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>The verdict rule that fired.</summary>
    public string FiredRuleId { get; set; } = string.Empty;

    /// <summary>The candidate the verdict was read for.</summary>
    public Guid? CandidateId { get; set; }

    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}

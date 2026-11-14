namespace FinanceSentry.Modules.Research.Domain.Coverage;

using FinanceSentry.Core.Auth;

/// <summary>
/// Append-only record of one coverage add or remove and the rule that fired (<see cref="Rule"/>, a stable id from
/// <see cref="CoverageRuleIds"/>). Nothing updates or deletes a row, so "why is / was this name covered" always
/// has an answer.
/// </summary>
public class CoverageJournalEntry : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Ticker { get; set; } = string.Empty;

    public CoverageAction Action { get; set; }

    /// <summary>The rule that fired, see <see cref="CoverageRuleIds"/>.</summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>The member's <see cref="CoverageMember.AddedReason"/> (on an add, the reason it was given).</summary>
    public CoverageReason Reason { get; set; }

    /// <summary>The facts the rule read, in words (verdict days, rank, penalty, days without an event, ...).</summary>
    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The name's correlation to the book when the rule fired; null when not evaluable.</summary>
    public decimal? CorrelationToBook { get; set; }

    /// <summary>The rank that decided a cap-bound add (scorecard points less the D1 penalty); null otherwise.</summary>
    public int? RankScore { get; set; }
}

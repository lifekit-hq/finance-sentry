namespace FinanceSentry.Modules.Research.Domain.Coverage;

using FinanceSentry.Core.Auth;

/// <summary>How a <see cref="CoverageMemo"/>'s model reply was read.</summary>
public enum CoverageMemoStatus
{
    /// <summary>The reply parsed into the memo shape; <see cref="CoverageMemo.Body"/> is that JSON.</summary>
    Structured,

    /// <summary>The call was paid for but the reply was not the memo shape; <see cref="CoverageMemo.Body"/> is the raw text.</summary>
    Unstructured,
}

/// <summary>
/// The research memo written once for a coverage entrant (discovery funnel stage 6, the funnel's only model call): what
/// the business is, why now, what would make the call wrong, crowding, correlation to the book and a verdict. One row per
/// coverage add (<see cref="JournalEntryId"/>), so a name is never paid for twice for the same entry. Logged only: nothing
/// reads a memo to place, size or change a position or to move a name in or out of coverage; the paper-pick scoreboard
/// grades the entrants before anyone sizes on them. The row records what the call consumed so the weekly spend cap is
/// enforced from stored facts.
/// </summary>
public class CoverageMemo : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Upper-case ticker of the entrant.</summary>
    public string Ticker { get; set; } = string.Empty;

    /// <summary>The <see cref="CoverageJournalEntry"/> add the memo is about; unique per user.</summary>
    public Guid JournalEntryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The model that wrote it, as the provider reported.</summary>
    public string ModelId { get; set; } = string.Empty;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    /// <summary>The call priced at the configured list price per million tokens (USD).</summary>
    public decimal CostUsd { get; set; }

    public CoverageMemoStatus Status { get; set; }

    /// <summary>The memo's verdict word (<c>Buy</c>, <c>Watch</c>, <c>Avoid</c>); null for an unstructured reply.</summary>
    public string? Verdict { get; set; }

    /// <summary>The memo JSON (<see cref="CoverageMemoStatus.Structured"/>) or the raw reply text.</summary>
    public string Body { get; set; } = string.Empty;
}

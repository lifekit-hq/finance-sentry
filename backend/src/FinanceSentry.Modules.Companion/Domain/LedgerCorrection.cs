namespace FinanceSentry.Modules.Companion.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// One time the owner told Ledger it was wrong, as Ledger recorded it: what it said, what was wrong with it, and the
/// owner's correction. A human reviews the log and folds what it teaches into Ledger's persona and tests; nothing
/// reads it back automatically. Counted per calendar month (UTC) against the owner's messages to Ledger.
/// </summary>
public sealed class LedgerCorrection : IUserOwned
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public string AgentSaid { get; set; } = string.Empty;

    public string WhatWasWrong { get; set; } = string.Empty;

    public string OwnerCorrection { get; set; } = string.Empty;

    /// <summary>When the owner made the correction; the month it counts in.</summary>
    public DateTimeOffset CorrectedAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

namespace FinanceSentry.Modules.Research.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// A cached "Ledger's read" — the agent-generated narrative for one user's view of one ticker
/// (feature 421, US3). One row per (user, symbol); regenerating overwrites in place.
/// </summary>
public class AssetLedgerRead : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Narrative { get; set; } = string.Empty;

    /// <summary>
    /// Digest of the dossier facts the narrative was generated from. A mismatch against the
    /// current dossier means the underlying data moved and the cached copy is stale.
    /// </summary>
    public string SourceFingerprint { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

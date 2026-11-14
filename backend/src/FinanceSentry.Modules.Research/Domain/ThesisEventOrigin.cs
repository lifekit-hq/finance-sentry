namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Who made a write: the owner's own session (<see cref="User"/>) or Ledger acting on its service token
/// (<see cref="Ledger"/>). Stored as text on the <see cref="ThesisEvent"/> journal; the origin shown on a
/// thesis, candidate or watchlist entry is the origin of its latest journal event.
/// </summary>
public enum ThesisEventOrigin
{
    User,
    Ledger,
}

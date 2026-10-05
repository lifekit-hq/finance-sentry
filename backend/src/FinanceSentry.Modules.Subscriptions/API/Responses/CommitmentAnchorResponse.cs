namespace FinanceSentry.Modules.Subscriptions.API.Responses;

/// <summary>
/// The latest charge under a picked transaction's commitment key: what an added row anchors on,
/// so the Add dialog can pre-fill the same amount the row will start from.
/// </summary>
/// <param name="Amount">Native amount of the latest charge, in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO code of the account the latest charge belongs to.</param>
/// <param name="Date">Date of the latest charge.</param>
/// <param name="ChargeCount">Charges under the key up to <paramref name="Date"/>.</param>
/// <param name="Cadence">Cadence read from their spacing, or null with fewer than two charges.</param>
public record CommitmentAnchorResponse(
    decimal Amount,
    string Currency,
    DateOnly Date,
    int ChargeCount,
    string? Cadence);

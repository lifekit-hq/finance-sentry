namespace FinanceSentry.Modules.Research.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// The user's own fair-value target for one symbol, shown on the asset dossier's "Is it cheap?" bar next
/// to the analyst target. One row per (user, symbol); setting it again overwrites in place. The price is
/// in the asset's quote currency (the one the dossier's price and analyst target are shown in).
/// </summary>
public class AssetPriceTarget : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public decimal TargetPrice { get; set; }

    /// <summary>The quote currency the target was set in; null when the dossier had no price currency.</summary>
    public string? Currency { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

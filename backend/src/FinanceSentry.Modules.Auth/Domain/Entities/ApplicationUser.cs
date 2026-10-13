using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Legacy: the Google subject from before Google sign-in moved to Identity's external-login table. Kept (and
    /// no longer written) so the column stays for the additive migration that copied it into that table.
    /// </summary>
    public string? GoogleId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>Light/dark choice: <c>system</c> (follow the OS), <c>light</c> or <c>dark</c>.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>The person's brand colour as <c>#rrggbb</c>; null means the app's own seed.</summary>
    public string? ThemeSeed { get; set; }

    /// <summary>How far the seed tints surfaces, 0-1; null means the tokens engine's default.</summary>
    public double? ThemeIntensity { get; set; }

    public bool EmailAlerts { get; set; } = true;
    public bool LowBalanceAlerts { get; set; } = true;
    public decimal LowBalanceThreshold { get; set; } = 500m;
    public bool SyncFailureAlerts { get; set; } = true;

    /// <summary>
    /// Also raise the daily analyst rating-change alert for watchlist names. Held names alert regardless;
    /// this is the opt-in for the names the user only follows (pick 6 of #825).
    /// </summary>
    public bool WatchlistAnalystAlerts { get; set; }

    /// <summary>Fraction of the FIRE target withdrawn per year (the classic 4% rule default).</summary>
    public decimal SafeWithdrawalRate { get; set; } = 0.04m;

    /// <summary>Real (inflation-adjusted) annual return assumed for the FIRE projection.</summary>
    public decimal RealAnnualReturn { get; set; } = 0.05m;
}

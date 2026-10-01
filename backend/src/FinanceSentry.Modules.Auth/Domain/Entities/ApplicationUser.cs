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
    public string Theme { get; set; } = "system";
    public bool EmailAlerts { get; set; } = true;
    public bool LowBalanceAlerts { get; set; } = true;
    public decimal LowBalanceThreshold { get; set; } = 500m;
    public bool SyncFailureAlerts { get; set; } = true;

    /// <summary>Fraction of the FIRE target withdrawn per year (the classic 4% rule default).</summary>
    public decimal SafeWithdrawalRate { get; set; } = 0.04m;

    /// <summary>Real (inflation-adjusted) annual return assumed for the FIRE projection.</summary>
    public decimal RealAnnualReturn { get; set; } = 0.05m;
}

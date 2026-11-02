namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;

/// <summary>
/// The two inputs of the payday-transfer figure that only the person knows: the fixed monthly outflow to cover on top of
/// the card's amount owed, and the step the suggested EUR transfer rounds up to. One row per user; a user without one
/// reads <see cref="PaydayTransferDefaults"/>.
/// </summary>
public class PaydayTransferSettings : Entity, IUserOwned
{
    public Guid UserId { get; set; }

    /// <summary>Fixed monthly outflow, in UAH.</summary>
    public decimal FixedOutflowUah { get; set; } = PaydayTransferDefaults.FixedOutflowUah;

    /// <summary>The suggested transfer is the exact EUR figure rounded up to a multiple of this, in EUR.</summary>
    public decimal RoundingStepEur { get; set; } = PaydayTransferDefaults.RoundingStepEur;
}

public static class PaydayTransferDefaults
{
    public const decimal FixedOutflowUah = 15000m;

    public const decimal RoundingStepEur = 50m;

    /// <summary>Upper bound the settings accept; far above any real monthly outflow, it only stops a typo overflowing the column.</summary>
    public const decimal MaxAmount = 1_000_000_000m;
}

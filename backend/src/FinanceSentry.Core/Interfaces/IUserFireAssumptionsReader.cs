namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Cross-module read port over the user's FIRE-projection assumptions, stored on
/// <c>ApplicationUser</c> in the Auth module. Wealth consumes this instead of referencing
/// Auth directly.
/// </summary>
public interface IUserFireAssumptionsReader
{
    Task<FireAssumptions?> GetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// <paramref name="SafeWithdrawalRate"/> and <paramref name="RealAnnualReturn"/> are both
/// fractions (0.04 = 4%), user-editable via the profile endpoint, defaulting to 0.04 and 0.05.
/// </summary>
public sealed record FireAssumptions(decimal SafeWithdrawalRate, decimal RealAnnualReturn);

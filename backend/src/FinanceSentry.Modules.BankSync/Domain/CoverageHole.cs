namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;

/// <summary>
/// A period in which an account's day-to-day spend stopped reaching us while its syncs kept succeeding - the spend moved
/// to a card or account we do not see. Written by the coverage sentinel; budget and estimate readers query it
/// (<c>ICoverageHoleReader</c>) and skip the period rather than read it as a frugal one.
/// </summary>
public class CoverageHole : Entity, IUserOwned
{
    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>First day of the hole (the Monday of its first quiet week).</summary>
    public DateOnly From { get; set; }

    /// <summary>
    /// Last day of the hole (the Sunday before the first week the spend flowed again); null while the hole is open.
    /// </summary>
    public DateOnly? To { get; set; }

    /// <summary>The usual weekly day-to-day spend, in USD, the account fell from. The hole closes against it.</summary>
    public decimal BaselineWeeklySpendUsd { get; set; }

    public bool IsOpen => To is null;
}

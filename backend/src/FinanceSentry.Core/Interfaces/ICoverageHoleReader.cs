namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// A period in which an account's day-to-day spend stopped reaching us while its syncs kept succeeding: the spend moved
/// somewhere we do not see (a card or account that is not connected, cash). <see cref="To"/> is null while the hole is
/// still open. Both ends are inclusive days.
/// </summary>
public sealed record CoverageHoleSpan(Guid AccountId, DateOnly From, DateOnly? To)
{
    /// <summary>Whether <paramref name="day"/> falls inside the hole.</summary>
    public bool Covers(DateOnly day) => day >= From && (To is null || day <= To);

    /// <summary>Whether any day of <c>[from, to]</c> falls inside the hole.</summary>
    public bool Overlaps(DateOnly from, DateOnly to) => from <= (To ?? DateOnly.MaxValue) && to >= From;
}

/// <summary>
/// The published read of the coverage sentinel's holes. A budget or an estimate that learns from past months reads it to
/// skip the holes instead of treating a quiet period as a frugal one.
/// </summary>
public interface ICoverageHoleReader
{
    /// <summary>
    /// The user's holes that overlap <c>[from, to]</c>, open ones included, oldest first. Names the user explicitly, so
    /// it is safe from a job with no principal.
    /// </summary>
    Task<IReadOnlyList<CoverageHoleSpan>> GetHolesAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

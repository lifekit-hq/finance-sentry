namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Deletes every row one module holds for a person, for account closure. Each module that stores per-person data
/// registers one; the closure purge runs them all. Runs with no person in scope, so an implementation opts out of every
/// query filter (the Owner filter and any soft-delete filter alike) and keeps its own <c>UserId</c> predicate.
/// Idempotent: a second run finds nothing and returns 0.
/// </summary>
public interface IUserDataEraser
{
    /// <summary>The module's name, recorded with the count in the closure's audit record.</summary>
    string Module { get; }

    /// <summary>Deletes the person's rows and returns how many rows the delete statements removed.</summary>
    Task<int> EraseAsync(Guid userId, CancellationToken ct = default);
}

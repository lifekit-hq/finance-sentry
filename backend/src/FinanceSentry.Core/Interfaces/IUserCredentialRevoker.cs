namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Stops a module acting for a person whose account is being closed: deletes the provider keys and tokens it stores
/// for them, revokes them at the provider where the provider offers that, and stops their scheduled work. The rest of
/// the person's data stays until the purge (<see cref="IUserDataEraser"/>). Runs with no person in scope, so an
/// implementation opts out of the Owner query filter and keeps its own <c>UserId</c> predicate. Idempotent.
/// </summary>
public interface IUserCredentialRevoker
{
    /// <summary>The module's name, recorded with the outcome in the closure's audit record.</summary>
    string Module { get; }

    /// <summary>
    /// Deletes the person's stored credentials. A failed remote revoke is reported in the outcome, never thrown:
    /// closing the account must not depend on a provider being reachable.
    /// </summary>
    Task<CredentialRevocation> RevokeAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>What one module removed for a closing account, and the provider revokes that did not go through.</summary>
public sealed record CredentialRevocation(int Removed, IReadOnlyList<string> Failures)
{
    public static CredentialRevocation Of(int removed) => new(removed, []);
}

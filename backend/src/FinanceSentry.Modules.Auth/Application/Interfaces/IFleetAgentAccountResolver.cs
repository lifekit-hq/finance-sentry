namespace FinanceSentry.Modules.Auth.Application.Interfaces;

/// <summary>Names the local Owner account the fleet-agent identity acts as.</summary>
public interface IFleetAgentAccountResolver
{
    /// <summary>
    /// The active Owner account's id, or <c>null</c> when there is none: the configured account is missing, is not
    /// an Owner or is locked out, or no account is configured and the deployment has zero or several Owners.
    /// </summary>
    Task<Guid?> ResolveAsync(CancellationToken cancellationToken = default);
}

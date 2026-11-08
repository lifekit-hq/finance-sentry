namespace FinanceSentry.Core.Auth;

/// <summary>
/// The scopes the fleet-agent identity (a machine-to-machine application of the org identity provider) may carry on
/// the fleet resource, one per action class. A scope names what a token may do; money, account and ownership
/// writes have no scope at all, so no token can carry them. Apps check the scope their endpoint needs, never only
/// the token's validity: a disabled or revoked application still gets a token from the provider, one with no scope.
/// </summary>
public static class FleetScopes
{
    /// <summary>The claim type each granted scope is rebuilt under on the fleet principal (one claim per scope).</summary>
    public const string ClaimType = "fleet_scope";

    /// <summary>Reads of the owner's finance and health data.</summary>
    public const string Read = "fleet:read";

    /// <summary>Reversible recategorisation of transactions between spending categories.</summary>
    public const string Recategorise = "fleet:recategorise";
}

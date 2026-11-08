namespace FinanceSentry.Modules.Auth.API.Authentication;

/// <summary>
/// The fleet-agent identity, bound from <c>Auth:FleetAgent</c>: a machine-to-machine application of the org identity
/// provider (Logto) whose access tokens the API host accepts on the fleet endpoints only. Nothing here is a secret:
/// the application's client secret never reaches this app, which validates the tokens the provider signs.
/// </summary>
public sealed class FleetAgentOptions
{
    public const string SectionName = "Auth:FleetAgent";

    /// <summary>The API resource indicator the provider puts in the token's <c>aud</c>; only a name, nothing resolves it.</summary>
    public const string DefaultAudience = "https://fleet.lifekit.internal/api";

    /// <summary>The scheme name of the fleet-agent bearer handler.</summary>
    public const string Scheme = "FleetAgent";

    /// <summary>Turns the identity off without removing configuration; every fleet token is then refused with 401.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The token issuer (e.g. <c>https://host.tailnet.ts.net:3001/oidc</c>). Empty = the Auth:Oidc authority.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Address this API uses for discovery and the signing keys when it cannot reach <see cref="Authority"/>
    /// (a docker-network name). The issuer validated stays <see cref="Authority"/>. Empty = the Auth:Oidc one.
    /// </summary>
    public string BackchannelAuthority { get; set; } = string.Empty;

    /// <summary>Turn off only for a local, plain-HTTP provider. Null = the Auth:Oidc setting.</summary>
    public bool? RequireHttpsMetadata { get; set; }

    /// <summary>The audience a token must carry.</summary>
    public string Audience { get; set; } = DefaultAudience;

    /// <summary>
    /// Optional pin to the fleet-agent application's id: when set, only tokens whose <c>client_id</c> equals it are
    /// accepted. When empty, a token must still be a machine-to-machine one (<c>sub</c> equal to <c>client_id</c>), so a
    /// person's access token for the same audience never passes.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The Owner account the fleet acts as (its data is what the endpoints read and change). Empty = the one Owner
    /// the deployment has; with several Owners and no value every fleet token is refused.
    /// </summary>
    public Guid? ActAsUserId { get; set; }
}

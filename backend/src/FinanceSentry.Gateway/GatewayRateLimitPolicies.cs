namespace FinanceSentry.Gateway;

/// <summary>
/// Named rate-limiter policy identifiers referenced by YARP routes (via each route's
/// <c>RateLimiterPolicy</c> in <c>appsettings.json</c>) and registered on the ASP.NET Core
/// <c>RateLimiter</c> in <c>Program.cs</c>. Keeping them as constants avoids magic strings drifting
/// between the config and the middleware registration (feature 025, FR-004).
/// </summary>
public static class GatewayRateLimitPolicies
{
    /// <summary>Per-client limit on credential endpoints (login, refresh, logout, MCP token calls).</summary>
    public const string Auth = "auth";

    /// <summary>
    /// Per-client limit on the session bootstrap calls every launch makes (<c>GET /auth/me</c>,
    /// <c>GET /auth/methods</c>, <c>GET /auth/oidc/start</c>). A separate, higher budget so a few
    /// relaunches from one household NAT never exhaust the credential budget or fail the sign-in page.
    /// </summary>
    public const string AuthSession = "auth-session";
}

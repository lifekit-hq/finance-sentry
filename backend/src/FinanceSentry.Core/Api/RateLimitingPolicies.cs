namespace FinanceSentry.Core.Api;

/// <summary>
/// Rate limiting policy names used with the ASP.NET Core rate limiter. The policies are registered in the
/// API host's Program.cs via AddRateLimiter and attached to endpoints with <c>[EnableRateLimiting]</c>.
/// </summary>
public static class RateLimitingPolicies
{
    /// <summary>100 requests/min for authenticated users (per user ID).</summary>
    public const string Authenticated = "authenticated";

    /// <summary>10 requests/min for anonymous users (per resolved client address).</summary>
    public const string Anonymous = "anonymous";

    /// <summary>Exempt from all rate limiting (health checks).</summary>
    public const string Exempt = "exempt";

    /// <summary>5 problem reports/hour per user (the handler also holds the 20/day cap from the saved rows).</summary>
    public const string ProblemReport = "problem-report";
}

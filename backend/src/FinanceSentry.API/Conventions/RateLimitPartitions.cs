namespace FinanceSentry.API.Conventions;

using System.Threading.RateLimiting;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;

/// <summary>
/// Partition keys for the API's rate-limit policies. Both key on the client address the forwarded-headers
/// middleware resolved (<c>UseForwardedHeaders</c> runs first and honours only the hops named in
/// <c>ForwardedHeaders:KnownProxies</c> / <c>KnownNetworks</c>), so behind the gateway each caller is
/// limited on its own address rather than the gateway's.
/// </summary>
public static class RateLimitPartitions
{
    private const int DefaultAuthenticatedPermitPerMinute = 100;
    private const int DefaultAnonymousPermitPerMinute = 10;
    public const string AuthenticatedPermitKey = "RateLimiting:Authenticated:PermitPerMinute";
    public const string AnonymousPermitKey = "RateLimiting:Anonymous:PermitPerMinute";
    private const string UnknownClient = "unknown";

    public static RateLimitPartition<string> Authenticated(HttpContext context)
    {
        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.GetUserId() : null;
        var key = userId is { } id ? $"user:{id}" : $"ip:{ClientAddress(context)}";
        return FixedWindow(key, Permit(context, AuthenticatedPermitKey, DefaultAuthenticatedPermitPerMinute));
    }

    public static RateLimitPartition<string> Anonymous(HttpContext context)
        => FixedWindow($"ip:{ClientAddress(context)}", Permit(context, AnonymousPermitKey, DefaultAnonymousPermitPerMinute));

    public static RateLimitPartition<string> Exempt(HttpContext context)
        => RateLimitPartition.GetNoLimiter(RateLimitingPolicies.Exempt);

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit)
        => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });

    private static int Permit(HttpContext context, string key, int fallback)
        => context.RequestServices.GetService<IConfiguration>()?.GetValue(key, fallback) ?? fallback;

    private static string ClientAddress(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
}

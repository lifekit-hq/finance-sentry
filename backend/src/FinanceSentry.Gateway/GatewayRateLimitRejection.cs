using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FinanceSentry.Gateway;

/// <summary>
/// Gives a rate-limiter 429 a readable body. A bare 429 has no <c>Content-Type</c>, which Safari turns
/// into a download named after the last path segment (a zero-byte "start" file on <c>oidc/start</c>), so
/// a top-level navigation gets a small HTML page and everything else <c>application/problem+json</c>.
/// Both carry <c>Retry-After</c>.
/// </summary>
public static class GatewayRateLimitRejection
{
    private const int FallbackRetryAfterSeconds = 60;
    private const string HtmlContentType = "text/html; charset=utf-8";
    private const string ProblemContentType = "application/problem+json";

    private const string HtmlBody = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Too many attempts</title>
        </head>
        <body style="font-family: system-ui, sans-serif; margin: 2rem; line-height: 1.5;">
        <h1>Too many attempts</h1>
        <p>Too many attempts, try again in a minute.</p>
        </body>
        </html>
        """;

    public static async ValueTask WriteAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
            ? Math.Max(1, (int)Math.Ceiling(window.TotalSeconds))
            : FallbackRetryAfterSeconds;
        response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        if (WantsHtml(context.HttpContext.Request))
        {
            response.ContentType = HtmlContentType;
            await response.WriteAsync(HtmlBody, cancellationToken);
            return;
        }

        response.ContentType = ProblemContentType;
        await response.WriteAsync(
            $$"""{"type":"about:blank","title":"Too many requests","status":429,"detail":"Too many attempts, try again in a minute."}""",
            cancellationToken);
    }

    // A top-level navigation lists text/html before anything else (browsers send
    // "text/html,application/xhtml+xml,..."); fetch/XHR from the SPA asks for application/json or */*.
    private static bool WantsHtml(HttpRequest request)
    {
        var accept = request.GetTypedHeaders().Accept;
        if (accept.Count == 0)
        {
            return false;
        }

        var preferred = accept
            .OrderByDescending(a => a.Quality ?? 1.0)
            .First();
        return preferred.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase);
    }
}

namespace FinanceSentry.Infrastructure.Retry;

using System.Net;

/// <summary>
/// Classifies provider HTTP status codes as transient (429, 5xx) or permanent
/// (400, 401, 403, 404, 422). Retry is handled by the scheduled sync cycle, not inline.
/// </summary>
public static class RetryPolicies
{
    /// <summary>
    /// HTTP status codes that indicate a permanent failure — never retry these.
    /// </summary>
    private static readonly HashSet<HttpStatusCode> PermanentFailureCodes =
    [
        HttpStatusCode.BadRequest,          // 400 — validation error
        HttpStatusCode.Unauthorized,         // 401 — auth failure
        HttpStatusCode.Forbidden,            // 403 — auth failure
        HttpStatusCode.NotFound,             // 404 — resource not found
        HttpStatusCode.UnprocessableEntity,  // 422 — semantic validation error
    ];

    /// <summary>
    /// Returns true for HTTP status codes that indicate a transient (retryable) error.
    /// Returns false for permanent errors that should not be retried.
    /// </summary>
    public static bool IsTransientHttpError(HttpStatusCode statusCode)
    {
        if (PermanentFailureCodes.Contains(statusCode))
            return false;

        return statusCode == HttpStatusCode.TooManyRequests  // 429 rate limit
            || (int)statusCode >= 500;                       // 5xx server errors
    }

    /// <summary>
    /// Returns true if the status code represents a permanent failure that should
    /// immediately surface to the caller without retrying.
    /// </summary>
    public static bool IsPermanentFailure(HttpStatusCode statusCode)
        => PermanentFailureCodes.Contains(statusCode);
}

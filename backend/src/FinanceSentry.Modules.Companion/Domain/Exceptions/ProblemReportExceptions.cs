namespace FinanceSentry.Modules.Companion.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class ProblemReportRateLimitedException : ApiException
{
    public ProblemReportRateLimitedException(TimeSpan retryAfter)
        : base(429, "REPORT_RATE_LIMITED", "You have sent a lot of reports lately. Please try again later.")
    {
        RetryAfterSeconds = (int)Math.Ceiling(Math.Max(retryAfter.TotalSeconds, 1));
    }
}

public class ProblemReportNotAllowedException()
    : ApiException(403, "REPORT_NOT_ALLOWED", "Sign in with your lifekit account to report a problem.");

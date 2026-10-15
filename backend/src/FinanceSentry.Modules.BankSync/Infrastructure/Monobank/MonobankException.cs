using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

public class MonobankException(string errorCode, string message, int httpStatus = 500)
    : ApiException(httpStatus, errorCode, message)
{
    /// <summary>
    /// The wait Monobank's <c>Retry-After</c> asked for, when the failure was a rate limit that named one. Kept apart
    /// from <see cref="ApiException.RetryAfterSeconds"/>, which would also put a header on the API's own response.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }
}

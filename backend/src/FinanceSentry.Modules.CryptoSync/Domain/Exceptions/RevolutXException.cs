namespace FinanceSentry.Modules.CryptoSync.Domain.Exceptions;

/// <summary>
/// Revolut X API and credential failures (422 / INVALID_CREDENTIALS). An HTTP failure carries an
/// <see cref="HttpRequestException"/> with the status code as its inner exception, so the job
/// failure classifier can tell a throttled or 5xx run (transient) from a rejected key (sticky).
/// </summary>
public sealed class RevolutXException : CryptoExchangeException
{
    /// <summary>The HTTP status Revolut X answered with, when the failure was an HTTP response.</summary>
    public int? VenueStatusCode { get; }

    public RevolutXException(string message)
        : base(message)
    {
    }

    public RevolutXException(string message, Exception innerException)
        : base(message, innerException)
    {
        VenueStatusCode = innerException is HttpRequestException { StatusCode: { } status } ? (int)status : null;
    }
}

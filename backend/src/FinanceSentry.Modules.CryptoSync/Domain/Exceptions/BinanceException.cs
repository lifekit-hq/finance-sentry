namespace FinanceSentry.Modules.CryptoSync.Domain.Exceptions;

/// <summary>Binance API and credential failures (422 / INVALID_CREDENTIALS).</summary>
public class BinanceException : CryptoExchangeException
{
    public int? BinanceErrorCode { get; }

    /// <summary>The HTTP status Binance answered with, when the failure was an HTTP response.</summary>
    public int? VenueStatusCode { get; }

    public BinanceException(string message, int? binanceErrorCode = null, int? venueStatusCode = null)
        : base(message)
    {
        BinanceErrorCode = binanceErrorCode;
        VenueStatusCode = venueStatusCode;
    }

    public BinanceException(string message, Exception innerException, int? binanceErrorCode = null)
        : base(message, innerException)
    {
        BinanceErrorCode = binanceErrorCode;
    }
}

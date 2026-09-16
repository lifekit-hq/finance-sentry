namespace FinanceSentry.Modules.CryptoSync.Domain.Exceptions;

/// <summary>Binance API and credential failures (422 / INVALID_CREDENTIALS).</summary>
public class BinanceException : CryptoExchangeException
{
    public int? BinanceErrorCode { get; }

    public BinanceException(string message, int? binanceErrorCode = null)
        : base(message)
    {
        BinanceErrorCode = binanceErrorCode;
    }

    public BinanceException(string message, Exception innerException, int? binanceErrorCode = null)
        : base(message, innerException)
    {
        BinanceErrorCode = binanceErrorCode;
    }
}

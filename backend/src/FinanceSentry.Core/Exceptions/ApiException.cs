namespace FinanceSentry.Core.Exceptions;

public abstract class ApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public int? RetryAfterSeconds { get; protected init; }

    protected ApiException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    protected ApiException(int statusCode, string errorCode, string message, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

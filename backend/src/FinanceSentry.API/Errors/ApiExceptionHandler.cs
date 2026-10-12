namespace FinanceSentry.API.Errors;

using System.Globalization;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// Converts any exception that escapes the pipeline into the API's <see cref="ApiErrorBody"/> JSON shape.
/// Never exposes stack traces or technical details. The logs written here carry the request's W3C trace
/// id natively (Serilog reads it from <c>Activity.Current</c>).
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, body) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception [{ErrorCode}]: {Message}", body.ErrorCode, exception.Message);
        else
            logger.LogWarning("Handled exception [{ErrorCode}]: {Message}", body.ErrorCode, exception.Message);

        // Headers are already on the wire (e.g. the SSE chat stream failing mid-stream): the status line can no
        // longer change and a JSON body would corrupt the stream, so the exception is logged and nothing is written.
        if (httpContext.Response.HasStarted)
            return true;

        httpContext.Response.StatusCode = statusCode;
        if (exception is ApiException { RetryAfterSeconds: { } retryAfter })
            httpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        // CancellationToken.None: when the client disconnected the token is already cancelled and the write is moot.
        await httpContext.Response.WriteAsJsonAsync(body, CancellationToken.None);
        return true;
    }

    private static (int StatusCode, ApiErrorBody Body) Map(Exception exception) => exception switch
    {
        ValidationException validation => (
            StatusCodes.Status400BadRequest,
            ApiErrorBody.From(
                "Validation failed.", "VALIDATION_ERROR",
                validation.Errors.Select(e => e.ErrorMessage))),
        ApiException api => (api.StatusCode, new ApiErrorBody(api.Message, api.ErrorCode)),
        UnauthorizedAccessException => (
            StatusCodes.Status401Unauthorized, new ApiErrorBody("Authentication required.", "UNAUTHORIZED")),
        // A unique-key violation is the caller's duplicate, not an outage.
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => (
            StatusCodes.Status409Conflict, new ApiErrorBody("This record already exists.", "DATABASE_ERROR")),
        DbUpdateException => (
            StatusCodes.Status503ServiceUnavailable,
            new ApiErrorBody("Database unavailable. Try again in 1 minute.", "DATABASE_ERROR")),
        OperationCanceledException => (
            ClientClosedRequestStatusCode, new ApiErrorBody("Request was cancelled.", "REQUEST_CANCELLED")),
        _ => (
            StatusCodes.Status500InternalServerError,
            new ApiErrorBody("An unexpected error occurred. Please try again.", "INTERNAL_ERROR")),
    };

    private const int ClientClosedRequestStatusCode = 499;
}

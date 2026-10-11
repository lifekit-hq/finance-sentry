namespace FinanceSentry.Infrastructure.Logging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// ASP.NET Core middleware that extracts or generates a correlation ID for each request,
/// injects it into AsyncLocal storage (via CorrelationIdAccessor), and includes it in
/// the response headers for client-side tracing.
///
/// Header name: X-Correlation-ID (incoming) / X-Correlation-ID (outgoing)
/// </summary>
public class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger,
    CorrelationIdAccessor accessor)
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    private readonly RequestDelegate _next = next;
    private readonly ILogger<CorrelationIdMiddleware> _logger = logger;
    private readonly CorrelationIdAccessor _accessor = accessor;

    public async Task InvokeAsync(HttpContext context)
    {
        // Use incoming correlation ID if provided, else generate a new one
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("N");

        _accessor.SetCorrelationId(correlationId);

        // Echo correlation ID back to the client for end-to-end tracing
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        await _next(context);
    }
}

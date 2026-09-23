namespace FinanceSentry.Infrastructure.Observability.HealthChecks;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Renders the readiness report as the contract JSON:
/// <c>{ "status":"Healthy", "checks":[{ "name":"database","status":"Healthy" }, ... ] }</c>.
/// Each entry's <c>name</c> is the registered health-check name (e.g. <c>database</c>, <c>hangfire</c>),
/// so a failing dependency is named in the body (feeds the SC-003 availability panel). A check that
/// explains itself with a plain description (e.g. <c>migrations</c> naming the pending migrations)
/// carries it as <c>description</c>. Entries backed by an exception carry name and status only: driver
/// checks such as <c>database</c> put the exception message in the description, and this endpoint is
/// unauthenticated, so that text (host, port, database name, user) must not be published.
/// </summary>
public static class ReadinessResponseWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Exception is null ? entry.Value.Description : null,
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}

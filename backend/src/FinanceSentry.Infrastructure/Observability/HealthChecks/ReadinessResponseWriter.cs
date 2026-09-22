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
/// supplies a description (e.g. <c>migrations</c> naming the pending migrations) carries it as
/// <c>description</c>; entries without one omit the field.
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
                description = entry.Value.Description,
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}

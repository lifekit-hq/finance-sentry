using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Alerts.API.Responses;
using FinanceSentry.Modules.Alerts.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListActiveAlertsTool(
    IQueryHandler<GetAlertsQuery, AlertsPageResponse> alertsHandler,
    IIdentityResolver identity,
    ILogger<ListActiveAlertsTool> logger)
{
    private readonly IQueryHandler<GetAlertsQuery, AlertsPageResponse> _alertsHandler = alertsHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<ListActiveAlertsTool> _logger = logger;

    [McpServerTool(Name = "list_active_alerts")]
    [Description("Returns unread, unresolved alerts (Fired).")]
    public async Task<IReadOnlyList<ActiveAlertEntry>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        AlertsPageResponse response;
        try
        {
            response = await _alertsHandler.Handle(
                new GetAlertsQuery(userIdVal, "unread", 1, 100),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alerts query unavailable for user {UserId}; returning empty list.", userIdVal);
            return [];
        }

        return response.Items
            .Where(a => !a.IsResolved)
            .Select(a => new ActiveAlertEntry(
                a.Id.ToString(),
                a.Type,
                a.Severity,
                a.Title,
                a.Message,
                a.CreatedAt,
                "Fired"))
            .ToList();
    }
}

public sealed record ActiveAlertEntry(
    string AlertId,
    string Type,
    string Severity,
    string Title,
    string Message,
    DateTimeOffset FiredAt,
    string Status);

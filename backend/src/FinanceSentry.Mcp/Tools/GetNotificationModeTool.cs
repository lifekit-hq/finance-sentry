using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetNotificationModeTool(
    IQueryHandler<GetNotificationModeQuery, NotificationModeDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_notification_mode")]
    [Description("Reads how proactive you (the companion) should be for this user: the notification mode — quiet (no proactive outreach), digest (one daily roll-up), scan (periodic briefs — the default), or realtime (push the moment something material happens) — plus quiet-hours, the daily-digest hour, and the proactive rate cap. On-demand chat is always available regardless of mode; the mode governs only proactive outreach.")]
    public async Task<NotificationModeDto?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetNotificationModeQuery(effective.Value), cancellationToken);
    }
}

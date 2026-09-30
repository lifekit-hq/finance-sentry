using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class SetNotificationModeTool(
    ICommandHandler<SetNotificationModeCommand, NotificationModeDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "set_notification_mode")]
    [Description("Sets how proactive you should be, on the user's request — use this when Denys says things like 'go quiet', 'just a daily digest', or 'ping me in real time'. Mode is one of: quiet | digest | scan | realtime. Takes effect immediately (no redeploy). Only changes proactive outreach — you still answer whenever he messages you. This is the user's preference; do not change it on your own initiative.")]
    public async Task<NotificationModeDto?> ExecuteAsync(
        [Description("New mode: quiet | digest | scan | realtime.")] string mode,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new SetNotificationModeCommand(effective.Value, mode), cancellationToken);
    }
}

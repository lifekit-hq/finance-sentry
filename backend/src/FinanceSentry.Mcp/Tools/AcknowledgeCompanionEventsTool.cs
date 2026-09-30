using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Companion.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class AcknowledgeCompanionEventsTool(
    ICommandHandler<AcknowledgeCompanionEventsCommand, int> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "acknowledge_companion_events")]
    [Description("Marks companion events as delivered AFTER you have actually told Denys about them, so they don't resurface in the next pull, scan, or digest. Call this with the ids from get_pending_companion_events once you've delivered them. Only acknowledge what you actually delivered — unacked events will be shown again (at-least-once).")]
    public async Task<int> ExecuteAsync(
        [Description("The event ids you have delivered.")] Guid[] eventIds,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return 0;
        }

        return await handler.Handle(
            new AcknowledgeCompanionEventsCommand(effective.Value, eventIds ?? []), cancellationToken);
    }
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Events.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

public sealed record RecordEventVerdictResult(bool Recorded);

[McpServerToolType]
public sealed class RecordEventVerdictTool(
    ICommandHandler<RecordEventVerdictCommand, bool> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "record_event_verdict")]
    [Description("Records your judgement on one fired event so the events feed can show it. Call it AFTER judging a companion event (the eventId from the wake payload or get_pending_companion_events), whether you sent a message (notified=true) or decided it was immaterial (notified=false) - both are verdicts; an acknowledged event with no verdict shows as silence. Verdict is plain text, at most 2000 characters; a later call replaces the earlier one. Returns recorded=false for an event that is not yours, does not exist, was not captured from an alert (analyst-action events cannot carry a verdict - do not retry), or a blank verdict.")]
    public async Task<RecordEventVerdictResult> ExecuteAsync(
        [Description("The companion event id you judged.")] Guid eventId,
        [Description("Your verdict in plain text: what happened and what it means. Max 2000 characters.")] string verdict,
        [Description("true if you told the user about it, false if you judged it immaterial and stayed quiet.")] bool notified,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return new RecordEventVerdictResult(false);
        }

        var recorded = await handler.Handle(
            new RecordEventVerdictCommand(effective.Value, eventId, verdict, notified), cancellationToken);
        return new RecordEventVerdictResult(recorded);
    }
}

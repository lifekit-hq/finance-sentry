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
    [Description("Records your judgement on one fired event so the events feed can show it. The wake-up contract: pull pending events (get_pending_companion_events / the wake payload), judge EVERY one of them, call this tool once per event whether or not you sent anything - notified=true after you told the user, notified=false when you judged it immaterial and stayed quiet - then acknowledge. Silence is a recorded outcome, never an absence: a run with nothing worth sending still records one verdict per event and sends no message. A fresh session has no memory of its own; this call is the only durable record that an event was judged at all, so the next run can tell 'already judged, stay quiet' from 'never seen'. Verdict is plain text, at most 2000 characters, and states the claim you are making about the event - if the verdict asserts a number or a fact, phrase it as claim, data (the value plus when you fetched it), source (the tool that returned it), and confidence (low/med/high), one line, not a paragraph. A later call replaces the earlier one for the same event. Returns recorded=false for an event that is not yours, does not exist, or a blank verdict.")]
    public async Task<RecordEventVerdictResult> ExecuteAsync(
        [Description("The companion event id you judged.")] Guid eventId,
        [Description("Your verdict in plain text: what happened and what it means. For an asserted fact, phrase as claim / data (value + retrieval time) / source / confidence. Max 2000 characters.")] string verdict,
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

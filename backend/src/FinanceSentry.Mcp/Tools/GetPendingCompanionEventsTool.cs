using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetPendingCompanionEventsTool(
    IQueryHandler<GetPendingCompanionEventsQuery, CompanionEventsResult> handler,
    IIdentityResolver identity)
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    [McpServerTool(Name = "get_pending_companion_events")]
    [Description("Pulls the material events Finance Sentry has captured for this user that you have NOT yet delivered — risk-rule violations, sync failures, unusual spend, opportunities, thesis breaks, market-structure signals, and analyst actions on held names. Each carries a kind, subject, severity, a short summary, and a reference id you can resolve via the other tools. Use this on your periodic scan (scan mode) or when woken (realtime), and for the daily roll-up (digest). Read-only — after you deliver them to Denys, call acknowledge_companion_events so they don't resurface. Empty is honest emptiness, never fabricate. The wake-up contract: every event this call returns gets exactly one record_event_verdict call before you finish the run, notified or not — an empty list still ends the run cleanly with nothing to record and no message sent.")]
    public async Task<CompanionEventsResult?> ExecuteAsync(
        [Description("Max results, default 25, max 100.")] int limit = DefaultLimit,
        [Description("Include events held for the daily digest (use true when composing the digest).")] bool includeHeldForDigest = false,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var cappedLimit = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
        return await handler.Handle(
            new GetPendingCompanionEventsQuery(effective.Value, cappedLimit, includeHeldForDigest), cancellationToken);
    }
}

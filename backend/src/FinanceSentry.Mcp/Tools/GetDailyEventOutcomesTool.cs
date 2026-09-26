using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetDailyEventOutcomesTool(
    IQueryHandler<GetDailyEventOutcomesQuery, DailyEventOutcomesResponse> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_daily_event_outcomes")]
    [Description("The accountability view for one UTC day (feature 687): every companion event that fired that day, of any kind - not just the five alert types get_event_calendar's fired feed covers - with what happened to it: fired (it happened), judged (a verdict was recorded, sent or withheld), sent (notified=true), withheld (notified=false - judged immaterial and deliberately not sent). A day with silent=fired-minus-judged events has verdicts still pending; awaiting/not_delivered/silent outcomes show up per-item even though they are not counted in judged. Use this to audit a run: after a wake-up, the day's judged count should equal its fired count. Empty is honest emptiness for a day with nothing captured, never fabricate.")]
    public async Task<DailyEventOutcomesResponse?> ExecuteAsync(
        [Description("The UTC calendar day to report on, e.g. 2026-09-26. Defaults to today (UTC).")] DateOnly? date = null,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return await handler.Handle(new GetDailyEventOutcomesQuery(effective.Value, day), cancellationToken);
    }
}

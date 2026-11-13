using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListCoverageEventsTool(
    IQueryHandler<ListCoverageEventsQuery, CoverageEventDigest> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_coverage_events")]
    [Description("The coverage disposition digest: every deterministic hit (a filing, an earnings date, a news cluster) on a name in the coverage universe since `sinceUtc` (ISO 8601, default 24 hours ago) and what the pipeline did about it, so you read one list on the pulse instead of being woken per event. Each event shows at, ticker, source (filing/earnings/news), reference, summary, disposition (Kept, Removed when a coverage remove rule fired, Pending when its review did not finish), rescored (the candidate score was re-run, deterministic, no model), reviewNotice (the CandidateReviewDue or ThesisReviewDue it raised, if any), verdict (the latest scorecard verdict word), rule and detail (why a name was removed), and whether the name is held, watchlisted or carries a thesis. changes are the coverage adds and removes in the same span, each with the rule that fired. Events are newest first, capped at `limit` (default and maximum 200); total always counts every match and truncated says the list was cut. Read-only. Empty is honest emptiness: nothing happened to a covered name in the window.")]
    public async Task<CoverageEventDigest> ExecuteAsync(
        [Description("Only events at or after this instant (ISO 8601 UTC). Omit for the last 24 hours.")] DateTimeOffset? sinceUtc = null,
        [Description("Most events to return, newest first. Omit for the maximum (200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            var now = DateTimeOffset.UtcNow;
            return new CoverageEventDigest(sinceUtc ?? now.AddDays(-1), now, 0, 0, 0, false, [], []);
        }

        return await handler.Handle(new ListCoverageEventsQuery(effective.Value, sinceUtc, limit), cancellationToken);
    }
}

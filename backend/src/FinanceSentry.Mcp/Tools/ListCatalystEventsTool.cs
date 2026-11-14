using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListCatalystEventsTool(
    IQueryHandler<ListCatalystEventsQuery, CatalystEventDigest> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_catalyst_events")]
    [Description("The catalyst log of the discovery funnel: every 8-K filing that carries a watched item (1.01 material agreement, 2.02 results, 5.02 officer or director change, 8.01 other events) on a name of the reference universe, detected by the daily EDGAR scan since `sinceUtc` (ISO 8601, default 7 days ago), and what the pipeline did about it. Each event shows detectedAt, ticker, accession, items, filingDate, summary, documentUrl, source (fulltext = EDGAR full-text search, submissions = the per-ticker fallback), disposition (Nominated = the name was outside coverage and entered the candidate path with a 'catalyst: 8-K item ...' nomination tag, which the coverage rules then score; Covered = already in coverage, handed to the coverage event review; SkippedRemoved = the name left coverage and the filing is no newer than the removal) and tag. A nomination is not an add: the stage 1 coverage rules decide that. Newest first, capped at `limit` (default and maximum 200); total counts every match. Read-only; no model is called.")]
    public async Task<CatalystEventDigest> ExecuteAsync(
        [Description("Only events detected at or after this instant (ISO 8601 UTC). Omit for the last 7 days.")] DateTimeOffset? sinceUtc = null,
        [Description("Most events to return, newest first. Omit for the maximum (200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            var now = DateTimeOffset.UtcNow;
            return new CatalystEventDigest(sinceUtc ?? now.AddDays(-7), now, 0, false, []);
        }

        return await handler.Handle(new ListCatalystEventsQuery(effective.Value, sinceUtc, limit), cancellationToken);
    }
}

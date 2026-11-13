using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListCoverageTool(
    IQueryHandler<ListCoverageQuery, CoverageListView> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_coverage")]
    [Description("Lists the coverage universe, \"our own index\": the capped list of companies (maxMembers, 100 by default) the pipeline maintains with no one's confirmation. Held and watchlisted names are always members; a Scan or Ledger candidate joins by reading Buy or StrongBuy on two days within 14 days, or by a Ledger nomination with a decision note; a name that is not held or watchlisted and carries no active thesis leaves after two consecutive Avoid verdicts, an expired candidate, or 60 days without an event and no dated catalyst. Each member shows addedReason (Held, Watchlist, Screen, Ledger), addedRule (the rule that added it), whether it is held, watchlisted or carries a thesis, lastEventAt, lastScoreId, correlationToBook (63-day, rule D1; null when the bars cannot support it), and removeAfter (when the no-event rule would remove it; null for a protected name). recentChanges are the latest adds and removes, each with the rule that fired. A coverage name may carry a Ledger thesis, not only a scorecard. Use explain_coverage for one ticker.")]
    public async Task<CoverageListView> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return new CoverageListView(0, 0, false, [], []);
        }

        return await handler.Handle(new ListCoverageQuery(effective.Value), cancellationToken);
    }
}

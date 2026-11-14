using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ExplainCoverageTool(
    IQueryHandler<ExplainCoverageQuery, CoverageExplanation> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "explain_coverage")]
    [Description("Explains why a ticker is or is not in the coverage universe, rule by rule: a summary sentence; the member row if covered (addedReason, addedRule, lastEventAt, correlationToBook, removeAfter); every add rule (held, watchlist, ledger_nomination, buy_twice_in_window) with whether it is met now and the facts it read; every remove rule (avoid_twice, candidate_expired, no_event) with whether it applies (it does not for a held, watchlisted or thesis name), whether it is met, and the facts; the cap; rank (the scorecard sub-score mean less the rule-D1 correlation penalty that orders cap-bound adds); the stored verdict readings; and the journal of the ticker's adds and removes. Read-only; deterministic.")]
    public async Task<CoverageExplanation?> ExecuteAsync(
        [Description("The ticker, e.g. 'AVGO'.")] string ticker,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null || string.IsNullOrWhiteSpace(ticker))
        {
            return null;
        }

        return await handler.Handle(new ExplainCoverageQuery(effective.Value, ticker), cancellationToken);
    }
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RunThesisMonitorTool(
    ICommandHandler<RunThesisMonitorCommand, ThesisMonitorRunSummary> monitorHandler,
    IQueryHandler<ListThesisBreaksQuery, IReadOnlyList<ThesisBreakView>> breaksHandler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "run_thesis_monitor")]
    [Description(
        "Re-evaluates the caller's active theses now (same deterministic code path as the scheduled job; "
        + "persists break-state changes and raises/resolves alerts as a side effect) AND returns the "
        + "resulting breaks in the same call. summary.unmonitorable lists every thesis with a trigger the "
        + "run could not evaluate: status UNMONITORABLE (no trigger evaluable) or PARTIALLY_MONITORABLE, and "
        + "per blind trigger its metric, periodType, subject ticker, reason (e.g. no_fundamentals, "
        + "insufficient_periods) and, for fundamentals metrics, the data coverage behind it. Such a thesis is "
        + "NOT intact - it is unwatched on those triggers; say so. For a read-only view that does NOT "
        + "re-evaluate or fire alerts, use list_thesis_breaks instead.")]
    public async Task<ThesisMonitorResult?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var summary = await monitorHandler.Handle(new RunThesisMonitorCommand(effective.Value), cancellationToken);
        var breaks = await breaksHandler.Handle(new ListThesisBreaksQuery(effective.Value), cancellationToken);

        return new ThesisMonitorResult(summary, breaks);
    }
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Analytics.API.Responses;
using FinanceSentry.Modules.Analytics.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RunAnalyticsQueryTool(
    IQueryHandler<RunAnalyticsQuery, AnalyticsQueryResponse> queryHandler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "run_analytics_query")]
    [Description(
        "Runs a single read-only SELECT over the curated per-user analytics views and returns the rows "
        + "plus the exact SQL executed. Use this for exploratory/ad-hoc structured questions that no "
        + "dedicated tool covers (e.g. 'weeks my discretionary spend was 30% above my 3-month average'). "
        + "Call describe_query_schema first to learn the exact views + columns. Only a single SELECT/WITH "
        + "is allowed — writes, DDL, and multi-statement are rejected; the query is scoped to you (never "
        + "another user's data) and bounded by a time + row budget. NOT for authoritative numbers (net "
        + "worth, risk verdicts, holdings totals) — those come from their dedicated tools.")]
    public async Task<AnalyticsQueryResponse?> ExecuteAsync(
        [Description("A single read-only SELECT over the analytics.v_* curated views.")] string sql,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await queryHandler.Handle(new RunAnalyticsQuery(effective.Value, sql), cancellationToken);
    }
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetIpsDiffTool(
    IQueryHandler<GetIpsDiffQuery, IpsDiffDto?> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_ips_diff")]
    [Description("Returns what changed between two versions of the user's Investment Policy Statement, field by field (goals, horizon, risk tolerance / capacity / maxDrawdownTolerancePct / riskMeasuredAt, allocation targets, rebalancing rule, contributions, sell discipline, cooling-off, exclusions, review cadence), each with its before and after value as JSON (null = unset). With no versions it compares the current version against the one before it. Null when the statement has no earlier version or a requested version does not exist. Use it to show the owner exactly how a re-measurement or amendment moved the policy.")]
    public async Task<IpsDiffDto?> ExecuteAsync(
        [Description("Version to compare from. Omit for the version before toVersion.")] int? fromVersion = null,
        [Description("Version to compare to. Omit for the current version.")] int? toVersion = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetIpsDiffQuery(effective.Value, fromVersion, toVersion), cancellationToken);
    }
}

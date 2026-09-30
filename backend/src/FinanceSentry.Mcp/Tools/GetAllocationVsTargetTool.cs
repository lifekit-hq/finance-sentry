using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetAllocationVsTargetTool(
    IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_allocation_vs_target")]
    [Description("Compares the user's ACTUAL portfolio allocation (brokerage + crypto + cash, in USD, bucketed by asset class) against their IPS target allocation, applying the IPS rebalancing bands (the 5/25 rule by default). Returns per-asset-class drift with a status of Within / OverBand (trim) / UnderBand (add) / Unplanned (held but not in policy) plus a needsRebalance flag — the data behind the rebalancing ceremony. When the user has no IPS yet, HasIps is false and only the current allocation is returned. Defaults to the authenticated MCP identity.")]
    public async Task<AllocationDriftDto?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetAllocationDriftQuery(effective.Value), cancellationToken);
    }
}

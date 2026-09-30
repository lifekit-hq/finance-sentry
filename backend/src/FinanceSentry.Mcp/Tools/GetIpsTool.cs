using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetIpsTool(
    IQueryHandler<GetIpsQuery, IpsDto?> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_ips")]
    [Description("Returns the user's current Investment Policy Statement — the written strategy: goals, time horizon, risk tolerance vs capacity, target asset allocation with rebalancing bands, contribution plan, sell discipline, and constraints. Every proactive recommendation should be measured against this. Returns null when no IPS exists yet; in that case run the onboarding interview and call save_ips to co-author one. Defaults to the authenticated MCP identity.")]
    public async Task<IpsDto?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetIpsQuery(effective.Value), cancellationToken);
    }
}

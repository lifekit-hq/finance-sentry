using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetPostmortemPacketTool(
    IQueryHandler<GetPostmortemPacketQuery, PostmortemPacketDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_postmortem_packet")]
    [Description("Compiles every terminal lifecycle event in a review period (decision notes, entry/exit prices, excess return) plus rejected-candidate counterfactuals from the same period, for Denys's periodic process review. Compiles the record; does not judge it.")]
    public async Task<PostmortemPacketDto?> ExecuteAsync(
        [Description("Period start date (inclusive).")] DateOnly periodStart,
        [Description("Period end date (inclusive).")] DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetPostmortemPacketQuery(effective.Value, periodStart, periodEnd), cancellationToken);
    }
}

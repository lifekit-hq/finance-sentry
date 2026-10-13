using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListRecommendationsTool(
    IQueryHandler<GetRecommendationsQuery, RecommendationsResult> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_recommendations")]
    [Description("The owner's Decisions queue: pending proposals oldest first (action, size, order text, rationale, evidence, trigger, expiry), and those closed in the last 30 days newest first with their outcome - status Confirmed, Rejected (with the owner's reason, the counterfactual), Expired or Superseded; execution Executed/Unexecuted (whether a matching fill of the owner's own followed within 7 days); and the name's return vs the S&P 500 since the decision. Read it before proposing, so you supersede deliberately rather than by accident, and when reviewing your own track record.")]
    public async Task<RecommendationsResult?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetRecommendationsQuery(effective.Value), cancellationToken);
    }
}

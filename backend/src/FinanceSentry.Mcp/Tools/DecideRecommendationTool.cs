using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class DecideRecommendationTool(
    ICommandHandler<DecideRecommendationCommand, RecommendationDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "decide_recommendation")]
    [Description("Records the owner's verdict on a pending proposal - only when the owner has told you their decision in this conversation; never decide on their behalf. verdict Confirm records the decision and nothing else: Finance Sentry never places, stages or transmits the order - give the owner the returned order text to place at the broker themselves. verdict Reject needs the owner's real reason in note; it is kept as the counterfactual. Both write a price-stamped decision-journal event (feeding get_track_record and get_postmortem_packet); a confirmed Exit also closes the ticker's thesis in the journal and a confirmed Trim marks it trimmed. Returns the decided proposal; errors when it is not pending or has expired.")]
    public async Task<RecommendationDto?> ExecuteAsync(
        [Description("The proposal id from list_recommendations.")] Guid id,
        [Description("Confirm or Reject.")] string verdict,
        [Description("Optional note on a confirm; on a reject, the owner's reason (required). Max 2000 characters.")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var parsed = Enum.TryParse<RecommendationVerdict>(verdict?.Trim(), ignoreCase: true, out var v) && Enum.IsDefined(v)
            ? v
            : throw new InvalidRecommendationException("verdict must be Confirm or Reject.");
        return await handler.Handle(new DecideRecommendationCommand(effective.Value, id, parsed, note), cancellationToken);
    }
}

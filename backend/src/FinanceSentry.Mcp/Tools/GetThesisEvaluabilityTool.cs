using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetThesisEvaluabilityTool(
    IQueryHandler<GetThesisEvaluabilityQuery, IReadOnlyList<ThesisEvaluabilityReport>> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_thesis_evaluability")]
    [Description(
        "Reports, per thesis, how many invalidation triggers are actually evaluable versus rejected for an "
        + "unsupported metric or missing direction/period scaffolding. Use this to find a thesis that appears "
        + "monitored but whose falsifiers never run. A thesis's narrativeFalsifiers are listed beside the "
        + "triggers as 'model-checked, not monitored': no job evaluates them, and they do not count toward coverage.")]
    public async Task<IReadOnlyList<ThesisEvaluabilityReport>?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetThesisEvaluabilityQuery(effective.Value), cancellationToken);
    }
}

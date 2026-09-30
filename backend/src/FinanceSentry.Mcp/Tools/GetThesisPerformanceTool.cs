using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetThesisPerformanceTool(
    IQueryHandler<GetThesisPerformanceQuery, ThesisPerformanceResult?> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_thesis_performance")]
    [Description("Returns absolute/benchmark/excess return (gross and net of cost/tax) for a thesis since its Created event through the latest live quote. Pass id or ticker; returns null if neither resolves a thesis.")]
    public async Task<ThesisPerformanceResult?> ExecuteAsync(
        [Description("Thesis id.")] Guid? id = null,
        [Description("Thesis ticker (used when id is not supplied; resolves the most recently created matching thesis).")] string? ticker = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetThesisPerformanceQuery(effective.Value, id, ticker), cancellationToken);
    }
}

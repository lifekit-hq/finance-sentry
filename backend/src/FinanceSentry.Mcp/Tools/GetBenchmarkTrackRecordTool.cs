using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetBenchmarkTrackRecordTool(
    IQueryHandler<GetBenchmarkTrackRecordQuery, BenchmarkTrackRecordDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_benchmark_track_record")]
    [Description("Answers 'are we beating the S&P 500' from the stored weekly track record: book, sleeve and per-thesis return vs SPY over 1M / 3M / 1Y / since inception, with the excess return, a net-of-friction excess only where the cost basis is verified (netGate), and the sustained-underperformance flag and run count. Cite these stored figures; never estimate relative performance yourself. Uncovered windows carry null returns — say the history is too short rather than filling the gap.")]
    public async Task<BenchmarkTrackRecordDto?> ExecuteAsync(
        [Description("Optional filter: 'Book', 'Sleeve' or 'Thesis'.")] string? scope = null,
        [Description("Optional filter: '1M', '3M', '1Y' or 'SinceInception'.")] string? window = null,
        [Description("Optional: narrow thesis rows to one ticker (book and sleeve rows are kept).")] string? ticker = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new GetBenchmarkTrackRecordQuery(effective.Value, scope, window, ticker), cancellationToken);
    }
}

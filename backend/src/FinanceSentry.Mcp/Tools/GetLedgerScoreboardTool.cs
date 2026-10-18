using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetLedgerScoreboardTool(
    IQueryHandler<GetLedgerScoreboardQuery, LedgerScoreboardDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_ledger_scoreboard")]
    [Description("Ledger scoreboard (an arena of one): over the same 30-day windows, Ledger's decided proposals as proposed (as if followed and executed), the owner's decisions as decided, and paper picks by source (User/Ledger/Scan), each as excess over the S&P 500 (the zero line): decided/executed counts, mean and median excess in points, the share that beat the S&P 500, the worst row, window start, gross and net of friction. Below 30 scored decisions a contestant reads 'too few to tell skill from luck (n of 30)'; n counts decisions, not overlapping days. Expired and superseded proposals are listed, never scored.")]
    public async Task<LedgerScoreboardDto?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetLedgerScoreboardQuery(effective.Value, DateTimeOffset.UtcNow), cancellationToken);
    }
}

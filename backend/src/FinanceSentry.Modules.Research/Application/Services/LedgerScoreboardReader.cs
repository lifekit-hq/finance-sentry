namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="ILedgerScoreboardReader"/> impl over Research's own <see cref="GetLedgerScoreboardQuery"/>.</summary>
public sealed class LedgerScoreboardReader(
    IQueryHandler<GetLedgerScoreboardQuery, LedgerScoreboardDto> scoreboard) : ILedgerScoreboardReader
{
    public async Task<LedgerScoreboardReading> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var board = await scoreboard.Handle(new GetLedgerScoreboardQuery(userId, DateTimeOffset.UtcNow), ct);
        var ledger = board.Contestants.Single(c => c.Contestant == GetLedgerScoreboardQueryHandler.LedgerAsProposed);
        return new LedgerScoreboardReading(
            ledger.Decided, ledger.Scored, ledger.Gross.MeanExcessPct, ledger.TooFewToTell, board.MinimumSampleSize);
    }
}

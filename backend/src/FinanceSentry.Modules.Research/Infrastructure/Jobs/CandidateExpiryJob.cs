namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily entry point that expires Active opportunity candidates past their TTL (US3/R9). Delegates
/// to <see cref="ExpireCandidatesCommand"/> so the schedule and any on-demand path share one code
/// path. Expired candidates are never deleted — they stay queryable for 020 counterfactuals. Then stamps every paper
/// pick that reached its horizon (<see cref="StampPaperPickHorizonsCommand"/>, #986) - after the expiry, whose
/// price-stamped <c>Expired</c> event already serves as the horizon point of a pick expiring that day.
/// </summary>
public sealed class CandidateExpiryJob(
    ICommandHandler<ExpireCandidatesCommand, ExpireCandidatesResult> handler,
    ICommandHandler<StampPaperPickHorizonsCommand, int> horizons,
    ILogger<CandidateExpiryJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var asOf = DateTimeOffset.UtcNow;
        var result = await handler.Handle(new ExpireCandidatesCommand(asOf), ct);
        logger.LogInformation("Candidate expiry run: {ExpiredCount} candidates expired", result.ExpiredCount);

        var stamped = await horizons.Handle(new StampPaperPickHorizonsCommand(asOf), ct);
        logger.LogInformation("Paper-pick horizon run: {StampedCount} picks stamped", stamped);
    }
}

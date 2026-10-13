namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily outcome scoring for decided proposals (#1069): matches the owner's own fills to mark a confirmed proposal
/// Executed or Unexecuted, and scores it against the S&amp;P 500.
/// </summary>
public sealed class RecommendationOutcomeJob(
    ICommandHandler<ScoreRecommendationOutcomesCommand, int> handler,
    ILogger<RecommendationOutcomeJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var count = await handler.Handle(new ScoreRecommendationOutcomesCommand(DateTimeOffset.UtcNow), ct);
        logger.LogInformation("Recommendation outcomes: {Count} proposals scored", count);
    }
}

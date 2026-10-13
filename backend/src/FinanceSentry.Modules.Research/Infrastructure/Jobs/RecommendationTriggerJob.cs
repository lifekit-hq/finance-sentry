namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily rule sweep that queues Exit proposals for held names whose thesis broke and Trim proposals for names over
/// the single-position cap (#1069). Silent: it only fills the Decisions queue.
/// </summary>
public sealed class RecommendationTriggerJob(
    ICommandHandler<RunRecommendationTriggersCommand, int> handler,
    ILogger<RecommendationTriggerJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var count = await handler.Handle(new RunRecommendationTriggersCommand(), ct);
        logger.LogInformation("Recommendation rule sweep: {Count} proposals queued", count);
    }
}

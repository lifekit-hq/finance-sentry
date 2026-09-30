namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily sweep for the scheduled policy review (#696). The job runs every day; each user's review
/// opens only when their policy statement's recorded cadence says it is due, so the cadence lives in
/// the statement, not in the cron. One user's failure never blocks the rest.
/// </summary>
public sealed class PolicyReviewJob(
    IIpsRepository ipsRepo,
    ICommandHandler<RunPolicyReviewCommand, PolicyReviewRunResult> handler,
    ILogger<PolicyReviewJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await ipsRepo.GetUserIdsWithCurrentIpsAsync(ct);

        foreach (var userId in userIds)
        {
            try
            {
                var result = await handler.Handle(new RunPolicyReviewCommand(userId), ct);
                if (result.Outcome == PolicyReviewRunOutcome.NotDue)
                {
                    logger.LogDebug(
                        "Policy review for user {UserId} not due until {DueAt:o}", userId, result.Schedule?.DueAt);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Policy review failed for user {UserId}", userId);
            }
        }
    }
}

namespace FinanceSentry.Modules.Wealth.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Monthly FIRE brief (#433 S7): for every active user, reads the same projection the dashboard tile
/// shows, composes it into a Telegram-sized brief with the gauge and every assumption spelled out, and
/// raises it as an Info alert. Delivery rides the existing alert → Companion → Ledger path, as the
/// weekly performance brief and the family statement do. A user without three complete months of
/// history gets no brief — the tile is hidden for them for the same reason.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class FireBriefJob(
    IBankingTotalsReader bankingTotals,
    IQueryHandler<GetFireProjectionQuery, FireProjectionResponse> projectionQuery,
    IAlertGeneratorService alerts,
    ILogger<FireBriefJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await bankingTotals.GetActiveUserIdsAsync(ct);

        var failures = new List<Exception>();
        foreach (var userId in userIds)
        {
            var error = await RunForUserAsync(userId, ct);
            if (error is not null)
            {
                failures.Add(error);
            }
        }

        // One user's brief failing must not cost the others theirs, but a run where every user failed
        // produced nothing at all — surface it so ConsecutiveFailureAlertFilter sees the streak.
        if (failures.Count > 0 && failures.Count == userIds.Count)
        {
            throw new AggregateException(
                $"FIRE brief failed for all {failures.Count} active user(s).", failures);
        }
    }

    private async Task<Exception?> RunForUserAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var projection = await projectionQuery.Handle(new GetFireProjectionQuery(userId), ct);
            var brief = FireBriefComposer.Compose(projection);
            if (brief is null)
            {
                return null;
            }

            await alerts.GenerateFireBriefAlertAsync(userId, brief.Headline, brief.Body, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FireBriefJob: failed to compose/alert for user {UserId}", userId);
            return ex;
        }
    }
}

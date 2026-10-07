namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Monthly family-clearing statement (#434 S5): for every active user, reads the prior
/// calendar month's <see cref="FamilyClearingStatement"/>, composes it into a Telegram-sized
/// digest, and raises it as an Info alert. Delivery rides the existing alert → Companion →
/// Ledger event-driven path — no new cron target, no new credential.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class FamilyClearingStatementJob(
    IBankingTotalsReader bankingTotals,
    IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement> statementQuery,
    IAlertGeneratorService alerts,
    IConfiguration configuration,
    ILogger<FamilyClearingStatementJob> logger)
{
    /// <summary>Public origin the brief's bullets link onto (<c>Companion:PublicBaseUrl</c>); empty = no links.</summary>
    private string? AppBaseUrl => configuration["Companion:PublicBaseUrl"];

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

        if (failures.Count > 0 && failures.Count == userIds.Count)
        {
            throw new AggregateException(
                $"Family clearing statement failed for all {failures.Count} active user(s).", failures);
        }
    }

    private async Task<Exception?> RunForUserAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var statement = await statementQuery.Handle(new GetFamilyClearingStatementQuery(userId), ct);
            var brief = FamilyClearingStatementComposer.Compose(statement, AppBaseUrl);
            await alerts.GenerateFamilyStatementAlertAsync(userId, brief.Headline, brief.Body, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "FamilyClearingStatementJob: failed to compose/alert for user {UserId}", userId);
            return ex;
        }
    }
}

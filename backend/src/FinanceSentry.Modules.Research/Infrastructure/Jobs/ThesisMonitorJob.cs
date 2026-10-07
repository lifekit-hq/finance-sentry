namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Scheduled entry point for the thesis-break monitor (FR-001). Runs the monitor for every user
/// that owns at least one thesis; schedule and on-demand (MCP, US2) share this command handler.
/// </summary>
public sealed class ThesisMonitorJob(
    IThesisRepository thesisRepo,
    ICommandHandler<RunThesisMonitorCommand, ThesisMonitorRunSummary> handler,
    ILogger<ThesisMonitorJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await thesisRepo.GetUserIdsWithThesesUnscopedAsync(ct);

        foreach (var userId in userIds)
        {
            var summary = await handler.Handle(new RunThesisMonitorCommand(userId), ct);
            logger.LogInformation(
                "Thesis monitor run for user {UserId}: {ThesesEvaluated} theses, " +
                "{TriggersEvaluated} triggers, {BreaksRaised} breaks raised, " +
                "{BreaksCleared} cleared, {Skipped} skipped, {Errors} errors, " +
                "{Unmonitorable} theses with unmonitorable triggers ({UnmonitorableTickers})",
                userId,
                summary.ThesesEvaluated,
                summary.TriggersEvaluated,
                summary.BreaksRaised,
                summary.BreaksCleared,
                summary.Skipped,
                summary.Errors,
                summary.Unmonitorable.Count,
                string.Join(", ", summary.Unmonitorable.Select(u => $"{u.Ticker}:{u.Status}")));
        }
    }
}

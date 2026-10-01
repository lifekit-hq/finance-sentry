namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

public record RunPolicyReviewCommand(Guid UserId) : ICommand<PolicyReviewRunResult>;

public enum PolicyReviewRunOutcome
{
    /// <summary>The user has no current policy statement, so there is nothing to review against.</summary>
    NoPolicy,

    /// <summary>The recorded cadence has not come round yet.</summary>
    NotDue,

    /// <summary>The book was empty or stale, so nothing was recorded and the next run retries.</summary>
    Deferred,

    /// <summary>A review ran, was recorded and was presented to the operator.</summary>
    Completed,
}

public sealed record PolicyReviewRunResult(
    PolicyReviewRunOutcome Outcome,
    PolicyReviewStatus? Schedule,
    Guid? ReviewId,
    int AdjustmentCount);

/// <summary>
/// Opens the scheduled policy review when the statement's recorded cadence says it is due (#696):
/// measures current positioning against the statement's bands, proposes adjustments with their
/// rationale, records the review (which stamps the statement's last-reviewed timestamp) and presents
/// the proposal through the alert → companion path. A review opened past the grace period also
/// reports the missed one. An empty or stale book defers the review, unrecorded, to the next run.
/// Recommend-only: nothing here places, stages or routes an order.
/// </summary>
public class RunPolicyReviewCommandHandler(
    IIpsRepository ipsRepo,
    IPolicyReviewRepository reviewRepo,
    IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto> driftHandler,
    IAlertGeneratorService alertGenerator,
    TimeProvider timeProvider,
    ILogger<RunPolicyReviewCommandHandler> logger)
    : ICommandHandler<RunPolicyReviewCommand, PolicyReviewRunResult>
{
    // The alert message column holds 1000 characters; the full rationale stays on the review record.
    private const int AlertSummaryMaxLength = 1000;

    public async Task<PolicyReviewRunResult> Handle(RunPolicyReviewCommand cmd, CancellationToken ct)
    {
        var versions = await ipsRepo.ListVersionsUnscopedAsync(cmd.UserId, ct);
        var ips = versions.FirstOrDefault(v => v.IsCurrent);
        if (ips is null)
            return new PolicyReviewRunResult(PolicyReviewRunOutcome.NoPolicy, null, null, 0);

        var now = timeProvider.GetUtcNow();
        var schedule = PolicyReviewSchedule.Evaluate(versions, now);
        if (!schedule.IsDue)
            return new PolicyReviewRunResult(PolicyReviewRunOutcome.NotDue, schedule, null, 0);

        // The lapse is reported on its own, before the catch-up review runs, so a review that then
        // fails still leaves the missed one visible.
        if (schedule.IsMissed)
            await ReportMissedAsync(cmd.UserId, schedule, ct);

        var drift = await driftHandler.Handle(new GetAllocationDriftQuery(cmd.UserId), ct);
        if (drift.IsStale || drift.TotalValueUsd <= 0)
        {
            logger.LogWarning(
                "Policy review for user {UserId} deferred: book is {State}; it will be retried on the next run",
                cmd.UserId, drift.IsStale ? "stale" : "empty");
            return new PolicyReviewRunResult(PolicyReviewRunOutcome.Deferred, schedule, null, 0);
        }

        var proposal = PolicyReviewProposer.Propose(ips, drift, schedule);

        // The risk re-measurement rides this review (#700): the request is part of the same proposal and
        // the same alert, not a second notification. Recording the answer is what clears it.
        var remeasurement = RiskMeasurementSchedule.RequestText(RiskMeasurementSchedule.Evaluate(ips, now), schedule.Cadence);
        var rationale = remeasurement is null ? proposal.Rationale : $"{proposal.Rationale}\n\n{remeasurement}";

        var review = new PolicyReview
        {
            Id = PolicyReview.DeriveId(cmd.UserId, ips.Id, schedule.DueAt),
            UserId = cmd.UserId,
            PolicyStatementId = ips.Id,
            PolicyStatementVersion = ips.Version,
            ReviewCadence = ips.ReviewCadence,
            DueAt = schedule.DueAt,
            CompletedAt = now,
            DaysOverdue = schedule.DaysOverdue,
            WasMissed = schedule.IsMissed,
            TotalValueUsd = drift.TotalValueUsd,
            Sleeves = [.. proposal.Sleeves],
            Adjustments = [.. proposal.Adjustments],
            Rationale = rationale,
        };

        await alertGenerator.GeneratePolicyReviewAlertAsync(
            cmd.UserId, review.Id, proposal.Adjustments.Count,
            AlertSummary(remeasurement is null ? proposal.Rationale : $"{remeasurement} {proposal.Rationale}"), ct);

        await reviewRepo.RecordAsync(review, ct);

        logger.LogInformation(
            "Policy review {ReviewId} completed for user {UserId} against statement v{Version}: {Adjustments} adjustment(s), missed={Missed}",
            review.Id, cmd.UserId, ips.Version, proposal.Adjustments.Count, schedule.IsMissed);

        return new PolicyReviewRunResult(PolicyReviewRunOutcome.Completed, schedule, review.Id, proposal.Adjustments.Count);
    }

    private async Task ReportMissedAsync(Guid userId, PolicyReviewStatus schedule, CancellationToken ct)
    {
        logger.LogWarning(
            "Policy review for user {UserId} was due {DueAt:o} and is {DaysOverdue} day(s) overdue",
            userId, schedule.DueAt, schedule.DaysOverdue);

        await alertGenerator.GeneratePolicyReviewMissedAlertAsync(
            userId, schedule.DueAt, schedule.DaysOverdue, schedule.Cadence, ct);
    }

    private static string AlertSummary(string rationale)
        => rationale.Length <= AlertSummaryMaxLength
            ? rationale
            : string.Concat(rationale.AsSpan(0, AlertSummaryMaxLength - 1), "…");
}

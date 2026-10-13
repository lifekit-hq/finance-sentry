namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;

public enum RecommendationVerdict
{
    Confirm,
    Reject,
}

/// <summary>
/// The owner's verdict on a pending proposal (#1069). Confirm records the decision and nothing else - Finance Sentry
/// never places, stages or transmits the order; the owner places it at the broker from the order text. Reject needs
/// the real reason, kept as the counterfactual. Both write a price-stamped journal event on the recommendation itself;
/// no thesis is touched. A proposal past its expiry is expired (and journalled) instead of decided.
/// </summary>
public sealed record DecideRecommendationCommand(
    Guid UserId, Guid RecommendationId, RecommendationVerdict Verdict, string? Note) : ICommand<RecommendationDto>;

public sealed class DecideRecommendationCommandHandler(
    IRecommendationRepository repo,
    IThesisEventRecorder recorder)
    : ICommandHandler<DecideRecommendationCommand, RecommendationDto>
{
    public async Task<RecommendationDto> Handle(DecideRecommendationCommand command, CancellationToken ct)
    {
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > Recommendation.MaxDecisionNoteLength })
        {
            throw new InvalidRecommendationException("The note is at most 2000 characters.");
        }

        if (command.Verdict == RecommendationVerdict.Reject && note is null)
        {
            throw new InvalidRecommendationException("Say why you are rejecting it - the reason is kept as the counterfactual.");
        }

        var recommendation = await repo.GetAsync(command.UserId, command.RecommendationId, ct)
            ?? throw new RecommendationNotFoundException();
        if (recommendation.Status != RecommendationStatus.Pending)
        {
            throw new RecommendationNotPendingException(recommendation.Status.ToString());
        }

        if (recommendation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await RecommendationExpiry.ExpireAsync(repo, recorder, recommendation, ct);
            throw new RecommendationNotPendingException(recommendation.Status.ToString());
        }

        var confirmed = command.Verdict == RecommendationVerdict.Confirm;
        recommendation.Status = confirmed ? RecommendationStatus.Confirmed : RecommendationStatus.Rejected;
        recommendation.DecidedAt = DateTimeOffset.UtcNow;
        recommendation.DecisionNote = note;
        recommendation.Execution = confirmed && recommendation.Action != RecommendationAction.Hold
            ? RecommendationExecution.Pending
            : RecommendationExecution.NotApplicable;
        await repo.UpdateAsync(recommendation, ct);

        await recorder.RecordAsync(
            command.UserId, ThesisSubjectType.Recommendation, recommendation.Id, recommendation.Ticker,
            confirmed ? ThesisEventType.Confirmed : ThesisEventType.Rejected, note, ct);

        return RecommendationDto.From(recommendation);
    }
}

namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Validation;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

public record SaveThesisCommand(
    Guid UserId,
    Guid? Id,
    string Ticker,
    string ThesisText,
    IReadOnlyList<ThesisDataPoint> KeyDataPoints,
    IReadOnlyList<ThesisCatalyst> Catalysts,
    IReadOnlyList<ThesisInvalidationTrigger> InvalidationTriggers,
    decimal? EntryPrice = null,
    string? DecisionNote = null) : ICommand<ThesisDto>;

public class SaveThesisCommandHandler(
    IThesisRepository repo,
    IThesisEventRecorder eventRecorder,
    ILogger<SaveThesisCommandHandler> logger)
    : ICommandHandler<SaveThesisCommand, ThesisDto>
{
    public async Task<ThesisDto> Handle(SaveThesisCommand cmd, CancellationToken ct)
    {
        ThesisTriggerVocabulary.Validate(cmd.InvalidationTriggers);

        InvestmentThesis thesis;
        var isNewThesis = cmd.Id is null;

        if (cmd.Id is { } id)
        {
            var existing = await repo.FindAsync(cmd.UserId, id, ct)
                ?? throw new ThesisNotFoundException();
            thesis = existing;
        }
        else
        {
            thesis = new InvestmentThesis { UserId = cmd.UserId };
        }

        thesis.Ticker = cmd.Ticker.Trim().ToUpperInvariant();
        thesis.ThesisText = cmd.ThesisText.Trim();
        thesis.EntryPrice = cmd.EntryPrice;
        thesis.KeyDataPoints = cmd.KeyDataPoints.ToList();
        thesis.Catalysts = cmd.Catalysts.ToList();
        thesis.InvalidationTriggers = cmd.InvalidationTriggers.ToList();

        await repo.UpsertAsync(thesis, ct);

        if (isNewThesis)
        {
            await TryRecordCreatedAsync(thesis, cmd.DecisionNote, ct);
        }

        return new ThesisDto(
            thesis.Id, thesis.Ticker, thesis.ThesisText,
            thesis.KeyDataPoints, thesis.Catalysts, thesis.InvalidationTriggers,
            thesis.CreatedAt, thesis.UpdatedAt, thesis.BrokenAt, thesis.BrokenReason, thesis.EntryPrice);
    }

    /// <summary>
    /// FR-001/FR-002: one Created event per thesis, never on update. The append runs *after* the
    /// thesis is committed, so letting it throw fails a call whose thesis is already in the
    /// database — the caller then retries and every retry writes another row, since a create
    /// carries a fresh id (issue #626). The journal is best-effort here for the same reason
    /// <c>RunThesisMonitorCommandHandler.TryRecordEventAsync</c> treats it as best-effort: a
    /// track-record side-effect must not decide whether the user's thesis was saved. Logged at
    /// error, because a lost Created event has no backfill path (unlike a missing quote, which
    /// <see cref="ThesisEventRecorder"/> marks PricesPending for the weekly job).
    /// </summary>
    private async Task TryRecordCreatedAsync(InvestmentThesis thesis, string? decisionNote, CancellationToken ct)
    {
        try
        {
            await eventRecorder.RecordAsync(
                thesis.UserId,
                ThesisSubjectType.Thesis,
                thesis.Id,
                thesis.Ticker,
                ThesisEventType.Created,
                decisionNote,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Created-event recording failed for thesis {ThesisId} ({Ticker}) — thesis saved without it",
                thesis.Id, thesis.Ticker);
        }
    }
}

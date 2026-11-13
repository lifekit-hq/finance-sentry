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
    string? DecisionNote = null,
    ThesisEventOrigin Origin = ThesisEventOrigin.User,
    string? Rationale = null,
    ThesisEvidenceRefs? Evidence = null) : ICommand<ThesisDto>;

public class SaveThesisCommandHandler(
    IThesisRepository repo,
    IThesisEventRecorder eventRecorder,
    ThesisOriginReader originReader,
    ILogger<SaveThesisCommandHandler> logger)
    : ICommandHandler<SaveThesisCommand, ThesisDto>
{
    public async Task<ThesisDto> Handle(SaveThesisCommand cmd, CancellationToken ct)
    {
        ThesisTriggerVocabulary.Validate(cmd.InvalidationTriggers);

        InvestmentThesis thesis;
        var isNewThesis = cmd.Id is null;
        ThesisVersion? prior = null;

        if (cmd.Id is { } id)
        {
            var existing = await repo.FindAsync(cmd.UserId, id, ct)
                ?? throw new ThesisNotFoundException();
            thesis = existing;
            prior = ThesisVersion.Of(existing);
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

        var current = ThesisVersion.Of(thesis);
        var revised = prior is not null && !prior.SameContentAs(current);

        await repo.UpsertAsync(thesis, ct);

        var audit = new ThesisAudit(prior, current, Trimmed(cmd.Rationale) ?? Trimmed(cmd.DecisionNote), cmd.Evidence);
        if (isNewThesis)
        {
            await TryRecordCreatedAsync(thesis, cmd, audit, ct);
        }
        else if (revised)
        {
            await RecordRevisedAsync(thesis, cmd, audit, ct);
        }

        var origin = isNewThesis || revised
            ? cmd.Origin
            : ThesisOriginReader.Resolve(await originReader.ForThesesAsync(cmd.UserId, [thesis.Id], ct), thesis.Id);

        return new ThesisDto(
            thesis.Id, thesis.Ticker, thesis.ThesisText,
            thesis.KeyDataPoints, thesis.Catalysts, thesis.InvalidationTriggers,
            thesis.CreatedAt, thesis.UpdatedAt, thesis.BrokenAt, thesis.BrokenReason, thesis.EntryPrice,
            origin.ToString());
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A revision's journal entry is what makes a Ledger change visible and reversible, so unlike the Created append
    /// it is not swallowed when Ledger made the change: the caller sees the failure and retries, instead of leaving a
    /// change nothing recorded. The owner's own edit keeps the best-effort treatment.
    /// </summary>
    private async Task RecordRevisedAsync(InvestmentThesis thesis, SaveThesisCommand cmd, ThesisAudit audit, CancellationToken ct)
    {
        try
        {
            await eventRecorder.RecordAuditedAsync(
                thesis.UserId, ThesisSubjectType.Thesis, thesis.Id, thesis.Ticker,
                ThesisEventType.Revised, cmd.Origin, audit, audit.Rationale, ct);
        }
        catch (Exception ex) when (cmd.Origin == ThesisEventOrigin.User)
        {
            logger.LogError(
                ex, "Revised-event recording failed for thesis {ThesisId} ({Ticker}) — thesis saved without it",
                thesis.Id, thesis.Ticker);
        }
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
    private async Task TryRecordCreatedAsync(
        InvestmentThesis thesis, SaveThesisCommand cmd, ThesisAudit audit, CancellationToken ct)
    {
        try
        {
            await eventRecorder.RecordAuditedAsync(
                thesis.UserId,
                ThesisSubjectType.Thesis,
                thesis.Id,
                thesis.Ticker,
                ThesisEventType.Created,
                cmd.Origin,
                audit,
                cmd.DecisionNote,
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

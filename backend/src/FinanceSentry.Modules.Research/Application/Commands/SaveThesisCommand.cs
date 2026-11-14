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
    ThesisEvidenceRefs? Evidence = null,
    // Null leaves the stored falsifiers as they are (a caller that does not know the field must not wipe them);
    // an empty list clears them; on create null means none.
    IReadOnlyList<string>? NarrativeFalsifiers = null) : ICommand<ThesisDto>;

public class SaveThesisCommandHandler(
    IThesisRepository repo,
    IThesisEventRecorder eventRecorder,
    IResearchWriteScope writeScope,
    ThesisOriginReader originReader,
    ILogger<SaveThesisCommandHandler> logger)
    : ICommandHandler<SaveThesisCommand, ThesisDto>
{
    public async Task<ThesisDto> Handle(SaveThesisCommand cmd, CancellationToken ct)
    {
        ThesisTriggerVocabulary.Validate(cmd.InvalidationTriggers);
        var narrativeFalsifiers = cmd.NarrativeFalsifiers is null
            ? null
            : NarrativeFalsifierRules.Normalize(cmd.NarrativeFalsifiers);

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
        if (narrativeFalsifiers is not null)
        {
            thesis.NarrativeFalsifiers = narrativeFalsifiers.ToList();
        }

        var current = ThesisVersion.Of(thesis);
        var revised = prior is not null && !prior.SameContentAs(current);

        var audit = new ThesisAudit(prior, current, Trimmed(cmd.Rationale) ?? Trimmed(cmd.DecisionNote), cmd.Evidence);
        await writeScope.RunAsync(cmd.Origin, async () =>
        {
            await repo.UpsertAsync(thesis, ct);
            if (isNewThesis)
            {
                await RecordAsync(thesis, cmd, ThesisEventType.Created, audit, cmd.DecisionNote, ct);
            }
            else if (revised)
            {
                await RecordAsync(thesis, cmd, ThesisEventType.Revised, audit, audit.Rationale, ct);
            }

            return thesis;
        }, ct);

        var origin = isNewThesis || revised
            ? cmd.Origin
            : ThesisOriginReader.Resolve(await originReader.ForThesesAsync(cmd.UserId, [thesis.Id], ct), thesis.Id);

        return new ThesisDto(
            thesis.Id, thesis.Ticker, thesis.ThesisText,
            thesis.KeyDataPoints, thesis.Catalysts, thesis.InvalidationTriggers,
            thesis.CreatedAt, thesis.UpdatedAt, thesis.BrokenAt, thesis.BrokenReason, thesis.EntryPrice,
            origin.ToString(), thesis.NarrativeFalsifiers.ToList());
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// FR-001/FR-002: one Created event per thesis, never on update; a Revised event for a content change. When Ledger
    /// made the change the append is part of the write's transaction and a failure rolls the thesis back, so the caller
    /// retries a clean write (the audit trail is what makes a Ledger change visible and reversible). The owner's own
    /// save keeps the journal best-effort: the append runs after the thesis is committed, and failing the call then
    /// would make the caller retry and write another row, since a create carries a fresh id (issue #626). Logged at
    /// error, because a lost event has no backfill path (unlike a missing quote, which
    /// <see cref="ThesisEventRecorder"/> marks PricesPending for the weekly job).
    /// </summary>
    private async Task RecordAsync(
        InvestmentThesis thesis, SaveThesisCommand cmd, ThesisEventType eventType, ThesisAudit audit, string? decisionNote,
        CancellationToken ct)
    {
        try
        {
            await eventRecorder.RecordAuditedAsync(
                thesis.UserId, ThesisSubjectType.Thesis, thesis.Id, thesis.Ticker,
                eventType, cmd.Origin, audit, decisionNote, ct);
        }
        catch (Exception ex) when (cmd.Origin == ThesisEventOrigin.User)
        {
            logger.LogError(
                ex, "{EventType}-event recording failed for thesis {ThesisId} ({Ticker}) — thesis saved without it",
                eventType, thesis.Id, thesis.Ticker);
        }
    }
}

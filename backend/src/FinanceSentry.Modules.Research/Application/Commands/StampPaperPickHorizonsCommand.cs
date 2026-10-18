namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Stamps each paper pick (#986) at its horizon: a price-stamped candidate <c>Snapshot</c> event for every candidate, in
/// any status, whose horizon has come and that has no horizon point yet (an <c>Expired</c> event at the TTL serves as
/// one). Runs daily with no person in scope; a pick past its grace window is left ungraded, never stamped late.
/// </summary>
public sealed record StampPaperPickHorizonsCommand(DateTimeOffset AsOf) : ICommand<int>;

public sealed class StampPaperPickHorizonsCommandHandler(
    ICandidateRepository candidates,
    IThesisEventRepository events,
    IThesisEventRecorder recorder)
    : ICommandHandler<StampPaperPickHorizonsCommand, int>
{
    private const string HorizonNote = "paper-pick horizon";

    public async Task<int> Handle(StampPaperPickHorizonsCommand command, CancellationToken ct)
    {
        var due = await candidates.ListCreatedBetweenUnscopedAsync(
            command.AsOf.AddDays(-(PaperPickHorizon.HorizonDays + PaperPickHorizon.GraceDays)),
            command.AsOf.AddDays(-PaperPickHorizon.HorizonDays),
            ct);

        var stamped = 0;
        foreach (var candidate in due)
        {
            var journal = await events.ListUnscopedAsync(candidate.UserId, candidate.Id, ct);
            if (PaperPickHorizon.Entry(journal) is null || PaperPickHorizon.HorizonPoint(candidate, journal) is not null)
            {
                continue;
            }

            await recorder.RecordAsync(
                candidate.UserId, ThesisSubjectType.Candidate, candidate.Id, candidate.Ticker,
                ThesisEventType.Snapshot, HorizonNote, ct);
            stamped++;
        }

        return stamped;
    }
}

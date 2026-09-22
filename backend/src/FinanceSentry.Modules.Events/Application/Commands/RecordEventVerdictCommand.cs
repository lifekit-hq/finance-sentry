namespace FinanceSentry.Modules.Events.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;

/// <summary>
/// Records the reader's judgement on one fired event (feature 049, US3). Returns true when written.
/// A foreign or unknown event id, an event not captured from an alert, or a blank / over-long
/// verdict writes nothing and returns false - the reader is told, never thrown at. The alert id is
/// persisted alongside so the verdict outlives the companion row.
/// </summary>
public sealed record RecordEventVerdictCommand(
    Guid UserId,
    Guid EventId,
    string Verdict,
    bool Notified) : ICommand<bool>;

public sealed class RecordEventVerdictCommandHandler(
    IEventDeliveryReader delivery,
    IEventVerdictRepository verdicts)
    : ICommandHandler<RecordEventVerdictCommand, bool>
{
    public async Task<bool> Handle(RecordEventVerdictCommand command, CancellationToken ct)
    {
        var text = command.Verdict?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > EventVerdict.MaxVerdictLength)
        {
            return false;
        }

        var evt = await delivery.FindAsync(command.UserId, command.EventId, ct);
        if (evt?.AlertId is null)
        {
            return false;
        }

        await verdicts.UpsertAsync(new EventVerdict
        {
            UserId = command.UserId,
            CompanionEventId = command.EventId,
            AlertId = evt.AlertId.Value,
            Verdict = text,
            Notified = command.Notified,
            RecordedAt = DateTimeOffset.UtcNow,
        }, ct);

        return true;
    }
}

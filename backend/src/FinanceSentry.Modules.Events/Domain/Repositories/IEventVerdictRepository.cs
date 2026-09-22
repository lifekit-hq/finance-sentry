namespace FinanceSentry.Modules.Events.Domain.Repositories;

public interface IEventVerdictRepository
{
    /// <summary>Insert, or replace the existing verdict for the same (user, companion event).</summary>
    Task UpsertAsync(EventVerdict verdict, CancellationToken ct = default);

    /// <summary>Verdicts recorded on any of the given alerts, whether or not their companion rows still exist.</summary>
    Task<IReadOnlyList<EventVerdict>> ListByAlertIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default);
}

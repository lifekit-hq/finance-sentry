namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Non-dismissed alerts of the given types, newest first, paged (adapter over the Alerts module).</summary>
public interface IFiredAlertReader
{
    Task<FiredAlertPage> ListAsync(
        Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default);
}

public sealed record FiredAlertPage(IReadOnlyList<FiredAlertRecord> Items, int TotalCount);

public sealed record FiredAlertRecord(
    Guid AlertId,
    string Type,
    string Severity,
    string Title,
    string Message,
    Guid? ReferenceId,
    string? ReferenceLabel,
    bool IsRead,
    bool IsResolved,
    DateTimeOffset CreatedAt);

namespace FinanceSentry.Modules.Alerts.Domain.Ports;

/// <summary>
/// Published read port (#673): a user's non-dismissed alerts of the given types, newest first,
/// paged, for cross-module readers such as the Events module. Implemented inside Alerts; the
/// FinanceSentry.Integration adapter reaches Alerts only through this interface.
/// </summary>
public interface IAlertsByTypeReader
{
    /// <summary>
    /// One page of alerts whose type is in <paramref name="types"/>. An empty type set yields an
    /// empty page; an out-of-range page or page size falls back to the Alerts defaults.
    /// </summary>
    Task<AlertsByTypeReadPage> ListAsync(
        Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>One page of alerts plus the total count across every page.</summary>
public sealed record AlertsByTypeReadPage(IReadOnlyList<AlertReadItem> Items, int TotalCount);

/// <summary>One alert as a cross-module reader sees it.</summary>
public sealed record AlertReadItem(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string Message,
    Guid? ReferenceId,
    string? ReferenceLabel,
    bool IsRead,
    bool IsResolved,
    DateTimeOffset CreatedAt);

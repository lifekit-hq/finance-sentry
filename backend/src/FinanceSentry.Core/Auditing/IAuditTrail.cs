namespace FinanceSentry.Core.Auditing;

/// <summary>
/// The append-only audit trail for the profile data the person declares (income, goals, career events) and for the
/// reads of it that leave the app through MCP or the in-app agent. Ids and field names only, never values - see
/// <see cref="AuditEntry"/>.
/// <para>
/// A write is recorded BEFORE it commits and a read BEFORE the data is returned, and a failure to record throws: a
/// sensitive action with no trail entry is worse than an action that did not happen. The cost is that an audit-store
/// outage stops those writes and reads, nothing else.
/// </para>
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default);
}

public static class AuditTrailExtensions
{
    public static Task RecordAsync(this IAuditTrail trail, AuditEntry entry, CancellationToken cancellationToken = default)
        => trail.RecordAsync([entry], cancellationToken);
}

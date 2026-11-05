using FinanceSentry.Core.Auditing;

namespace FinanceSentry.Modules.Auth.Domain.Entities;

/// <summary>
/// One row of the append-only audit trail: someone wrote to, or read through MCP or the in-app agent, a person's
/// declared profile data. Ids and field NAMES only, never a value - <see cref="AuditEntry"/> is the only way in and
/// refuses anything but identifiers.
/// <para>
/// It lives in the Auth module beside the closure record and the tokens, because it describes access to a person's
/// data and has to be there whichever module held the data. Rows are never updated; retention purges them by
/// <see cref="OccurredAt"/> and the account purge erases the subject's.
/// </para>
/// </summary>
public class AuditEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; private set; }

    /// <summary>Whose data it concerns.</summary>
    public Guid SubjectUserId { get; private set; }

    /// <summary>The person acted as; null for a background job.</summary>
    public Guid? ActorUserId { get; private set; }

    public AuditActorKind ActorKind { get; private set; }
    public AuditAction Action { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }

    /// <summary>The fields written or read, by name.</summary>
    public string[] FieldNames { get; private set; } = [];

    /// <summary>The W3C trace id of the request or job, for finding the log lines around the event.</summary>
    public string TraceId { get; private set; } = string.Empty;

    private AuditEvent() { }

    public AuditEvent(AuditEntry entry, DateTime occurredAt, string traceId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        OccurredAt = occurredAt;
        SubjectUserId = entry.SubjectUserId;
        ActorUserId = entry.ActorUserId;
        ActorKind = entry.ActorKind;
        Action = entry.Action;
        ResourceType = entry.ResourceType;
        ResourceId = entry.ResourceId;
        FieldNames = [.. entry.FieldNames];
        TraceId = traceId;
    }
}

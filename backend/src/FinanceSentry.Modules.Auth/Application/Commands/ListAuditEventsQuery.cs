using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// A page of the audit events concerning <paramref name="UserId"/>'s own data, newest first. <paramref name="Before"/> is
/// the previous page's last <c>OccurredAt</c> (exclusive); null starts at the newest.
/// </summary>
public record ListAuditEventsQuery(Guid UserId, DateTime? Before = null, int Take = ListAuditEventsQuery.DefaultTake)
    : IQuery<AuditEventPageDto>
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;
}

/// <summary>An audit event as the person reads it: who did what to which of their data, by id and field name, never a value.</summary>
public record AuditEventDto(
    Guid Id,
    DateTime OccurredAt,
    string ActorKind,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    IReadOnlyList<string> FieldNames,
    string TraceId);

/// <param name="NextBefore">Pass as <c>before</c> for the next page; null when this was the last.</param>
public record AuditEventPageDto(IReadOnlyList<AuditEventDto> Events, DateTime? NextBefore);

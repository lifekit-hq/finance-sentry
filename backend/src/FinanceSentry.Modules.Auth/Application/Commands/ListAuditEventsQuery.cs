using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// A page of the audit events concerning <paramref name="UserId"/>'s own data, newest first. <paramref name="Before"/>
/// and <paramref name="BeforeId"/> are the previous page's last <c>(OccurredAt, Id)</c> (exclusive): a batch's events
/// share one <c>OccurredAt</c>, so the id breaks the tie and a page boundary inside a batch loses nothing. Both null
/// starts at the newest.
/// </summary>
public record ListAuditEventsQuery(
    Guid UserId, DateTime? Before = null, Guid? BeforeId = null, int Take = ListAuditEventsQuery.DefaultTake)
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
/// <param name="NextBeforeId">Pass as <c>beforeId</c> beside <paramref name="NextBefore"/>; null when this was the last.</param>
public record AuditEventPageDto(IReadOnlyList<AuditEventDto> Events, DateTime? NextBefore, Guid? NextBeforeId);

namespace FinanceSentry.Modules.Companion.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// The user's undelivered companion events for the agent to deliver (feature 031, US2). Read-only —
/// does NOT mark delivered; the agent acks explicitly after delivering. Held-for-digest events are
/// demoted on purpose and belong to the digest: they are returned only when the caller both asks for
/// them and supplies a non-blank <paramref name="HeldOverrideReason"/>; the override is logged.
/// </summary>
public record GetPendingCompanionEventsQuery(
    Guid UserId, int Limit, bool IncludeHeldForDigest, string? HeldOverrideReason = null)
    : IQuery<CompanionEventsResult>;

public class GetPendingCompanionEventsQueryHandler(
    ICompanionEventRepository events,
    INotificationSettingRepository settings,
    ILogger<GetPendingCompanionEventsQueryHandler> logger)
    : IQueryHandler<GetPendingCompanionEventsQuery, CompanionEventsResult>
{
    public const string HeldWithheldNote =
        "Held-for-digest events were withheld: includeHeldForDigest requires a non-blank heldOverrideReason. An empty list here does not mean there are none.";

    private static readonly EventDisposition[] Undelivered =
        [EventDisposition.Pending, EventDisposition.Dispatched,
         EventDisposition.DeferredQuietHours, EventDisposition.SuppressedByRateLimit];

    public async Task<CompanionEventsResult> Handle(GetPendingCompanionEventsQuery query, CancellationToken ct)
    {
        var reason = query.HeldOverrideReason?.Trim();
        var includeHeld = query.IncludeHeldForDigest && !string.IsNullOrEmpty(reason);
        var refused = query.IncludeHeldForDigest && !includeHeld;
        if (includeHeld)
        {
            logger.LogWarning(
                "Held-for-digest override for {User}: reason \"{Reason}\"", query.UserId, reason);
        }
        else if (refused)
        {
            logger.LogWarning(
                "Held-for-digest events requested by {User} without an override reason; excluded", query.UserId);
        }

        var dispositions = includeHeld
            ? [.. Undelivered, EventDisposition.HeldForDigest]
            : Undelivered;

        var rows = await events.ListByDispositionAsync(query.UserId, dispositions, query.Limit, ct);
        var mode = (await settings.GetOrDefaultAsync(query.UserId, ct)).Mode;

        var dtos = rows
            .Select(e => new CompanionEventDto(
                e.Id, e.Kind.ToString(), e.Subject, e.Severity, e.Summary,
                e.ReferenceId, e.Disposition.ToString(), e.OccurredAt))
            .ToList();

        return new CompanionEventsResult(dtos, mode.ToString(), DateTimeOffset.UtcNow, refused ? HeldWithheldNote : null);
    }
}

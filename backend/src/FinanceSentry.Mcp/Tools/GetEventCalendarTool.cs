using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

/// <summary>The unified event calendar (feature 049): what is coming and what fired, with the recorded outcome per fired event.</summary>
public sealed record EventCalendarResult(
    IReadOnlyList<UpcomingEventDto> Upcoming,
    IReadOnlyList<FiredEventDto> Fired,
    IReadOnlyList<EventSourceStatusDto> Sources,
    DateOnly From,
    DateOnly To,
    DateTimeOffset RetrievedAt);

[McpServerToolType]
public sealed class GetEventCalendarTool(
    IQueryHandler<GetUpcomingEventsQuery, UpcomingEventsResult> upcoming,
    IQueryHandler<GetFiredEventsQuery, FiredEventsPageResponse> fired,
    IIdentityResolver identity)
{
    private const int DefaultDaysAhead = 14;
    private const int DefaultDaysBack = 7;
    private const int DefaultFiredLimit = 50;
    private const int MaxDaysAhead = 366;
    private const int MaxDaysBack = 366;

    [McpServerTool(Name = "get_event_calendar")]
    [Description("One calendar for the book: upcoming events (earnings, ex-dividend, derived filing due dates, macro calendar, thesis catalysts) over the next daysAhead days, plus the events that fired over the last daysBack days (earnings-ahead, filing-landed, news-cluster, market-structure moves, budget breaches) with each one's outcome: verdict, judged_immaterial, silent (acknowledged with nothing recorded), awaiting, or not_delivered. sources[] says which upcoming source was unavailable, so an empty day is not mistaken for a quiet one. Record your judgement on a fired event with record_event_verdict.")]
    public async Task<EventCalendarResult?> ExecuteAsync(
        [Description("Days ahead to include for upcoming events. Default 14, max 366.")] int daysAhead = DefaultDaysAhead,
        [Description("Days back to include for fired events. Default 7, max 366.")] int daysBack = DefaultDaysBack,
        [Description("Optional upcoming kinds filter: earnings, ex_dividend, filing_due, macro, thesis_catalyst. Default all.")] IReadOnlyList<string>? kinds = null,
        [Description("Maximum fired events returned, newest first. Default 50, max 100.")] int limit = DefaultFiredLimit,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ahead = Math.Clamp(daysAhead, 0, MaxDaysAhead);
        var back = Math.Clamp(daysBack, 0, MaxDaysBack);
        var firedLimit = Math.Clamp(limit, 1, GetFiredEventsQueryHandler.MaxPageSize);

        var upcomingResult = await upcoming.Handle(
            new GetUpcomingEventsQuery(effective.Value, today, today.AddDays(ahead), kinds), cancellationToken);

        var firedResult = await fired.Handle(
            new GetFiredEventsQuery(effective.Value, 1, firedLimit, null), cancellationToken);

        var since = new DateTimeOffset(today.AddDays(-back).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var firedInWindow = firedResult.Items.Where(f => f.OccurredAt >= since).ToList();

        return new EventCalendarResult(
            upcomingResult.Items, firedInWindow, upcomingResult.Sources, upcomingResult.From, upcomingResult.To, DateTimeOffset.UtcNow);
    }
}

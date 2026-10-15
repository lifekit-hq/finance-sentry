using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetMacroCalendarTool(
    IQueryHandler<GetMacroCalendarQuery, IReadOnlyList<MacroEventDto>> handler)
{
    [McpServerTool(Name = "get_macro_calendar")]
    [Description("Returns macro releases (FOMC, CPI, NFP, ECB) in a date range. Filter by region (US, EU), minimum importance (low/medium/high) and status. Each event carries eventAt (the release instant in UTC; eventDate/eventTime are the publisher's local clock), status ('scheduled' until eventAt, 'released' from then on) and eventKey (stable per release, e.g. macro:CPI:2026-10). To flag events in the next 24-48h during ledger-scan, pass status='scheduled' and keep only eventAt within the window: a released event is never upcoming, whatever its date. Announce each eventKey ahead of time at most once.")]
    public async Task<IReadOnlyList<MacroEventDto>> ExecuteAsync(
        [Description("Start date (inclusive). Defaults to today if omitted.")] DateOnly? from = null,
        [Description("End date (inclusive). Defaults to today+30 if omitted.")] DateOnly? to = null,
        [Description("Optional region filter (US, EU).")] IReadOnlyList<string>? regions = null,
        [Description("Minimum importance level (low, medium, high). Defaults to no filter.")] string? minImportance = null,
        [Description("Optional status filter: 'scheduled' (not yet released) or 'released'. Defaults to both.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromEff = from ?? today;
        var toEff = to ?? fromEff.AddDays(30);
        return await handler.Handle(new GetMacroCalendarQuery(fromEff, toEff, regions, minImportance, status), cancellationToken);
    }
}

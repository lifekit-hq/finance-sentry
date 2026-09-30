using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetEarningsCalendarTool(
    IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>> handler,
    IIdentityResolver identity)
{
    private const int DefaultWindowDays = 90;

    [McpServerTool(Name = "get_earnings_calendar")]
    [Description("Upcoming earnings-report dates and ex-dividend/dividend dates, fetched live from Yahoo Finance (no key). When 'tickers' is omitted, defaults to the authenticated user's own equity holdings plus watchlist — use this to warn ahead of an event (\"you hold NVDA, it reports in 2 days\"). EventType is one of earnings / ex_dividend / dividend. Dates default to the next 90 days. earnings entries carry isEstimate=true when Yahoo has not yet confirmed the date.")]
    public async Task<IReadOnlyList<EarningsEventDto>> ExecuteAsync(
        [Description("Optional explicit tickers (e.g. [\"NVDA\",\"AAPL\"]). Omit to use the user's holdings + watchlist.")] IReadOnlyList<string>? tickers = null,
        [Description("Start date (inclusive). Defaults to today.")] DateOnly? from = null,
        [Description("End date (inclusive). Defaults to today+90.")] DateOnly? to = null,
        [Description("Optional filter: earnings, ex_dividend, or dividend.")] string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        var hasExplicitTickers = tickers is { Count: > 0 };
        if (effective is null && !hasExplicitTickers)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromEff = from ?? today;
        var toEff = to ?? fromEff.AddDays(DefaultWindowDays);

        return await handler.Handle(
            new GetEarningsCalendarQuery(tickers, effective, fromEff, toEff, eventType), cancellationToken);
    }
}

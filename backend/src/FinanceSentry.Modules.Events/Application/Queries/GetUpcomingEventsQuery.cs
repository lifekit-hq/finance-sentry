namespace FinanceSentry.Modules.Events.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Exceptions;
using FinanceSentry.Modules.Events.Domain.Ports;
using Microsoft.Extensions.Logging;

/// <summary>
/// Upcoming events for a user in a window (feature 049, US1). <paramref name="From"/>/<paramref name="To"/>
/// default to today .. today + 90 days; <paramref name="Kinds"/> null or empty means every kind.
/// </summary>
public sealed record GetUpcomingEventsQuery(
    Guid UserId,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyCollection<string>? Kinds) : IQuery<UpcomingEventsResult>;

/// <summary>
/// Computes the calendar at read time from the sources Finance Sentry already holds: no table, no
/// job, no new feed. Each source runs in isolation - a throwing source contributes nothing and is
/// reported <c>unavailable</c> on the result, so an empty day is never mistaken for a quiet one.
/// Sources run sequentially on purpose: two of them share the scoped Research DbContext.
/// </summary>
public sealed class GetUpcomingEventsQueryHandler(
    IBrokerageHoldingsReader brokerage,
    IWatchlistReader watchlist,
    IUpcomingCorporateEventReader corporate,
    IMacroEventReader macro,
    IThesisCatalystReader theses,
    IPeriodicFilingReader filings,
    ILogger<GetUpcomingEventsQueryHandler> logger)
    : IQueryHandler<GetUpcomingEventsQuery, UpcomingEventsResult>
{
    public const int DefaultHorizonDays = 90;
    public const int MaxWindowDays = 366;

    private const string CorporateEarnings = "earnings";
    private const string CorporateExDividend = "ex_dividend";
    private const string CorporateSourceLabel = "Yahoo Finance";

    public async Task<UpcomingEventsResult> Handle(GetUpcomingEventsQuery query, CancellationToken ct)
    {
        var (from, to) = ResolveWindow(query.From, query.To);
        var kinds = ResolveKinds(query.Kinds);

        var items = new List<UpcomingEvent>();
        var sources = new List<EventSourceStatusDto>();

        var wantsCorporate = kinds.Contains(EventKind.Earnings) || kinds.Contains(EventKind.ExDividend);
        var wantsFilings = kinds.Contains(EventKind.FilingDue);

        IReadOnlyCollection<string> tickers = [];
        if (wantsCorporate || wantsFilings)
        {
            tickers = await ResolveTickersAsync(query.UserId, ct);
        }

        if (wantsCorporate)
        {
            await RunSourceAsync(EventSource.Corporate, sources, ct, async token =>
            {
                if (tickers.Count == 0)
                {
                    return;
                }

                foreach (var e in await corporate.GetForTickersAsync(tickers, from, to, token))
                {
                    var kind = e.EventType switch
                    {
                        CorporateEarnings => EventKind.Earnings,
                        CorporateExDividend => EventKind.ExDividend,
                        _ => null,
                    };
                    if (kind is null || !kinds.Contains(kind))
                    {
                        continue;
                    }

                    var title = kind == EventKind.Earnings ? $"Earnings: {e.Ticker}" : $"Ex-dividend: {e.Ticker}";
                    items.Add(new UpcomingEvent(
                        kind, e.Date, null, e.Ticker.ToUpperInvariant(), title, null, e.IsEstimate,
                        string.IsNullOrWhiteSpace(e.Source) ? CorporateSourceLabel : e.Source, null));
                }
            });
        }

        if (kinds.Contains(EventKind.Macro))
        {
            await RunSourceAsync(EventSource.Macro, sources, ct, async token =>
            {
                foreach (var m in await macro.QueryAsync(from, to, token))
                {
                    items.Add(new UpcomingEvent(
                        EventKind.Macro, m.Date, m.Time, m.Region, m.Event, $"{m.Region} · {m.Importance} importance",
                        false, m.Source, m.Id));
                }
            });
        }

        if (kinds.Contains(EventKind.ThesisCatalyst))
        {
            await RunSourceAsync(EventSource.Theses, sources, ct, async token =>
            {
                foreach (var c in await theses.ListActiveAsync(query.UserId, token))
                {
                    if (c.Date < from || c.Date > to)
                    {
                        continue;
                    }

                    items.Add(new UpcomingEvent(
                        EventKind.ThesisCatalyst, c.Date, null, c.Ticker.ToUpperInvariant(),
                        $"Thesis catalyst: {c.Ticker.ToUpperInvariant()}", c.Event, false, "thesis", c.ThesisId));
                }
            });
        }

        if (wantsFilings)
        {
            await RunSourceAsync(EventSource.Filings, sources, ct, async token =>
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                foreach (var ticker in tickers)
                {
                    var recent = await filings.GetRecentAsync(ticker, token);
                    var due = FilingDueCalculator.Next(recent, today);
                    if (due is null || due.DueDate < from || due.DueDate > to)
                    {
                        continue;
                    }

                    var symbol = ticker.ToUpperInvariant();
                    items.Add(new UpcomingEvent(
                        EventKind.FilingDue, due.DueDate, null, symbol, $"{due.Form} due: {symbol}",
                        $"Period ending {due.PeriodEnd:yyyy-MM-dd}", true, "SEC EDGAR", null));
                }
            });
        }

        var ordered = items
            .GroupBy(e => (e.Kind, e.Subject, e.Date))
            .Select(g => g.First())
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Time ?? TimeOnly.MaxValue)
            .ThenBy(e => e.Subject, StringComparer.Ordinal)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .Select(e => new UpcomingEventDto(
                e.Kind, e.Date, e.Time, e.Subject, e.Title, e.Detail, e.IsEstimate, e.Source, e.ReferenceId))
            .ToList();

        return new UpcomingEventsResult(ordered, from, to, sources);
    }

    private static (DateOnly From, DateOnly To) ResolveWindow(DateOnly? from, DateOnly? to)
    {
        var effectiveFrom = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveTo = to ?? effectiveFrom.AddDays(DefaultHorizonDays);
        if (effectiveTo < effectiveFrom)
        {
            throw new EventsWindowInvalidException("'to' is before 'from'.");
        }

        if (effectiveTo.DayNumber - effectiveFrom.DayNumber > MaxWindowDays)
        {
            throw new EventsWindowInvalidException($"window exceeds {MaxWindowDays} days.");
        }

        return (effectiveFrom, effectiveTo);
    }

    private static IReadOnlySet<string> ResolveKinds(IReadOnlyCollection<string>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            return EventKind.All;
        }

        return requested
            .Select(k => k.Trim().ToLowerInvariant())
            .Where(EventKind.All.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<IReadOnlyCollection<string>> ResolveTickersAsync(Guid userId, CancellationToken ct)
    {
        var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in await brokerage.GetHoldingsAsync(userId, ct))
        {
            if (AssetClassNormalizer.Normalize(h.InstrumentType) == AssetClassNormalizer.Equities)
            {
                tickers.Add(h.Symbol);
            }
        }

        foreach (var t in await watchlist.ListTickersAsync(userId, ct))
        {
            tickers.Add(t);
        }

        return tickers;
    }

    private async Task RunSourceAsync(
        string source, List<EventSourceStatusDto> statuses, CancellationToken ct, Func<CancellationToken, Task> read)
    {
        try
        {
            await read(ct);
            statuses.Add(new EventSourceStatusDto(source, EventSourceStatus.Ok));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Upcoming events: source {Source} unavailable", source);
            statuses.Add(new EventSourceStatusDto(source, EventSourceStatus.Unavailable));
        }
    }
}

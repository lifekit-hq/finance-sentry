namespace FinanceSentry.Modules.Radar.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Syncs the universe then ingests daily bars for every active ticker. Per-ticker isolated.</summary>
public sealed record IngestDailyBarsCommand : ICommand<IngestRunSummary>;

public sealed class IngestDailyBarsCommandHandler(
    IRadarUniverseService universe,
    IMarketHistorySource history,
    IDailyBarRepository bars,
    IOptions<RadarOptions> options,
    ILogger<IngestDailyBarsCommandHandler> logger)
    : ICommandHandler<IngestDailyBarsCommand, IngestRunSummary>
{
    private readonly RadarOptions _options = options.Value;

    public async Task<IngestRunSummary> Handle(IngestDailyBarsCommand command, CancellationToken cancellationToken)
    {
        var members = await universe.SyncAsync(cancellationToken);
        var latestDates = await bars.GetLatestDatesAsync(
            members.Select(m => m.Ticker).ToList(), cancellationToken);
        var scheduled = Schedule(members);

        var lookbackSince = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddDays(-CalendarDaysFor(_options.LookbackTradingDays));

        var ingested = 0;
        var barsAdded = 0;
        var failed = new List<string>();

        foreach (var member in scheduled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var since = latestDates.TryGetValue(member.Ticker, out var latest)
                    ? latest.AddDays(1)
                    : lookbackSince;

                var fetched = await history.GetDailyBarsAsync(member.Ticker, since, cancellationToken);
                if (fetched.Count == 0)
                {
                    ingested++;
                    continue;
                }

                var entities = fetched.Select(b => new DailyBar
                {
                    Ticker = member.Ticker,
                    Date = b.Date,
                    Open = b.Open,
                    High = b.High,
                    Low = b.Low,
                    Close = b.Close,
                    AdjClose = b.AdjClose,
                    Volume = b.Volume,
                }).ToList();

                barsAdded += await bars.UpsertRangeAsync(entities, cancellationToken);
                ingested++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Radar ingestion failed for {Ticker}; continuing.", member.Ticker);
                failed.Add(member.Ticker);
            }
        }

        return new IngestRunSummary(ingested, barsAdded, failed.Count, failed);
    }

    /// <summary>
    /// Book first: holdings, watchlist and the seed lenses are fetched before the stage-1 shortlist,
    /// so an upstream rate limit costs breadth rather than the freshness of what is actually owned.
    /// The shortlist is tens of names (#558), so unlike the rotating index budget it replaced, every
    /// scheduled member is fetched in the same run.
    /// </summary>
    private List<RadarUniverseMember> Schedule(IReadOnlyList<RadarUniverseMember> members)
    {
        var scheduled = members
            .OrderBy(m => m.Kind == UniverseKind.IndexConstituent ? 1 : 0)
            .ThenBy(m => m.Ticker, StringComparer.Ordinal)
            .ToList();

        logger.LogInformation(
            "Radar ingestion scheduling {Core} core and {Shortlisted} shortlisted tickers.",
            scheduled.Count(m => m.Kind != UniverseKind.IndexConstituent),
            scheduled.Count(m => m.Kind == UniverseKind.IndexConstituent));

        return scheduled;
    }

    // Convert a trading-day lookback to a calendar-day window with a weekend/holiday cushion (~1.5x).
    private static int CalendarDaysFor(int tradingDays) => (int)Math.Ceiling(tradingDays * 1.5);
}

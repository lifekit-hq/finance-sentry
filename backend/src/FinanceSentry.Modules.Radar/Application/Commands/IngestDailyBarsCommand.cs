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
        var scheduled = Schedule(members, latestDates);

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
    /// Core members (holdings, watchlist, seed lenses) are always ingested; broad-market constituents
    /// then fill <see cref="RadarOptions.BroadUniverseMaxIngestPerRun"/>, least fresh first, so a
    /// 500-name universe rotates across runs instead of stretching one run into a rate-limit wall.
    /// </summary>
    private List<RadarUniverseMember> Schedule(
        IReadOnlyList<RadarUniverseMember> members, IReadOnlyDictionary<string, DateOnly> latestDates)
    {
        var scheduled = members.Where(m => m.Kind != UniverseKind.IndexConstituent).ToList();

        var budget = _options.BroadUniverseMaxIngestPerRun;
        if (budget <= 0)
        {
            return scheduled;
        }

        var broad = members
            .Where(m => m.Kind == UniverseKind.IndexConstituent)
            .OrderBy(m => latestDates.TryGetValue(m.Ticker, out var latest) ? latest : DateOnly.MinValue)
            .ThenBy(m => m.Ticker, StringComparer.Ordinal)
            .Take(budget)
            .ToList();

        if (broad.Count > 0)
        {
            logger.LogInformation(
                "Radar ingestion scheduling {Core} core and {Broad} broad-universe tickers (budget {Budget}).",
                scheduled.Count, broad.Count, budget);
        }

        scheduled.AddRange(broad);
        return scheduled;
    }

    // Convert a trading-day lookback to a calendar-day window with a weekend/holiday cushion (~1.5x).
    private static int CalendarDaysFor(int tradingDays) => (int)Math.Ceiling(tradingDays * 1.5);
}

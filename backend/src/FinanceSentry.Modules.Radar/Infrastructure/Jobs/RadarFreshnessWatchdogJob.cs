namespace FinanceSentry.Modules.Radar.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// FR-017: flags stale Radar data. Structure reads already carry <c>stale=true</c> over stale bars;
/// this job additionally raises a freshness Alert. Unlike market signals, this is an operational
/// alert — it fires in every scanner mode, because the log-only calibration phase (FR-015) exists
/// to tune market thresholds, not to hide dead data.
/// </summary>
public sealed class RadarFreshnessWatchdogJob(
    IRadarUniverseRepository universe,
    IDailyBarRepository bars,
    IBankingTotalsReader bankingTotals,
    IAlertGeneratorService alerts,
    IOptions<RadarOptions> options,
    ILogger<RadarFreshnessWatchdogJob> logger)
{
    private static readonly Guid FreshnessReferenceId = new("00000000-0000-0000-0000-0000000f8e54");

    private readonly RadarOptions _options = options.Value;

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Broad-universe constituents are ingested on a rotating budget (#558), so an old bar there is
        // the designed cadence rather than a data outage — watching them would bury the real signal.
        var members = (await universe.ListActiveAsync(ct))
            .Where(m => m.Kind != UniverseKind.IndexConstituent)
            .ToList();
        if (members.Count == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latestDates = await bars.GetLatestDatesAsync(members.Select(m => m.Ticker).ToArray(), ct);

        var stale = members
            .Where(m => FreshnessEvaluator.IsStale(
                latestDates.TryGetValue(m.Ticker, out var d) ? d : null,
                today,
                _options.FreshnessMaxTradingDays))
            .Select(m => m.Ticker)
            .ToList();

        if (stale.Count == 0)
        {
            return;
        }

        logger.LogWarning("Radar freshness watchdog: {Count} stale tickers ({Tickers}).",
            stale.Count, string.Join(",", stale));

        var reason = $"{stale.Count} universe tickers have bars older than {_options.FreshnessMaxTradingDays} trading days";
        var userIds = await bankingTotals.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
        {
            await alerts.GenerateMarketStructureFreshnessAlertAsync(userId, FreshnessReferenceId, reason, ct);
        }
    }
}

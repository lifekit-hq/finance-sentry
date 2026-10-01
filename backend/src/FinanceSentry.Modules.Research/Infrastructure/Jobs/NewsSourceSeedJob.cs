namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Idempotently seeds the market-wide default news feeds and the TrendForce → DRAM thesis page source
/// (feature 030, T046/FR-007). Runs on a schedule (and startup) so the defaults exist without a data
/// migration; existing sources (matched by URL) are left untouched. The TrendForce source is only
/// seeded once a DRAM thesis exists — it is skipped gracefully otherwise.
/// <para>
/// It also repairs rows seeded with a URL we have since corrected (issue #318). Idempotency-by-URL
/// alone cannot fix a row whose URL is the thing that is wrong: #326 changed the TrendForce constant
/// but the deployed row kept pointing at the old marketing hub and kept failing, so the repair pass
/// below rewrites it in place and clears the failure state that had retired it.
/// </para>
/// </summary>
public sealed class NewsSourceSeedJob(
    INewsSourceRepository sources,
    ResearchDbContext research,
    ILogger<NewsSourceSeedJob> logger)
{
    private const string TrendForceUrl = "https://www.trendforce.com/presscenter/news";

    /// <summary>
    /// URLs this job has shipped for the TrendForce source in the past and must migrate off. The list
    /// is exact rather than a normalisation rule: "/presscenter/" is the marketing hub seeded by
    /// feature 030, and only rows we ourselves seeded wrong should be rewritten — a source Denys
    /// registered by hand keeps whatever URL he chose.
    /// </summary>
    private static readonly string[] LegacyTrendForceUrls =
    [
        "https://www.trendforce.com/presscenter/",
    ];

    private static readonly (string Name, string Url)[] MarketWideRssDefaults =
    [
        ("Yahoo Finance Top Stories", "https://finance.yahoo.com/news/rssindex"),
        ("MarketWatch Top Stories", "http://feeds.marketwatch.com/marketwatch/topstories/"),
    ];

    private static readonly string[] DramKeywords = ["DRAM", "HBM", "NAND", "memory"];

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var seeded = 0;

        foreach (var (name, url) in MarketWideRssDefaults)
        {
            if (await sources.GetByUrlAsync(url, ct) is not null)
            {
                continue;
            }

            await sources.AddAsync(new NewsSource { Name = name, Kind = NewsSourceKind.Rss, Url = url }, ct);
            seeded++;
        }

        await RepairLegacyTrendForceSourcesAsync(ct);

        seeded += await SeedTrendForceAsync(ct);
        logger.LogInformation("NewsSourceSeedJob seeded {Count} new sources", seeded);
    }

    /// <summary>
    /// Re-points any TrendForce row still on a legacy URL at the canonical press-release list and
    /// revives it. When a canonical row already exists the legacy row is dropped instead — its URL is
    /// taken, and the only state it holds that the canonical row does not is the failure history we
    /// are discarding anyway.
    /// </summary>
    private async Task RepairLegacyTrendForceSourcesAsync(CancellationToken ct)
    {
        foreach (var legacyUrl in LegacyTrendForceUrls)
        {
            var legacy = await sources.GetByUrlAsync(legacyUrl, ct);
            if (legacy is null)
            {
                continue;
            }

            if (await sources.GetByUrlAsync(TrendForceUrl, ct) is not null)
            {
                await sources.RemoveAsync(legacy, ct);
                logger.LogInformation(
                    "Removed superseded TrendForce source on legacy url {LegacyUrl}", legacyUrl);
                continue;
            }

            legacy.Url = TrendForceUrl;
            NewsSourceHealthTracker.ClearFailures(legacy);
            await sources.UpdateAsync(legacy, ct);
            logger.LogInformation(
                "Repaired TrendForce source {LegacyUrl} -> {Url} and cleared its failure state",
                legacyUrl, TrendForceUrl);
        }
    }

    private async Task<int> SeedTrendForceAsync(CancellationToken ct)
    {
        if (await sources.GetByUrlAsync(TrendForceUrl, ct) is not null)
        {
            return 0;
        }

        var dramThesisId = await FindDramThesisIdAsync(ct);
        if (dramThesisId is null)
        {
            logger.LogInformation("TrendForce source not seeded — no DRAM thesis found yet (skipped gracefully)");
            return 0;
        }

        await sources.AddAsync(new NewsSource
        {
            Name = "TrendForce Press Center",
            Kind = NewsSourceKind.Page,
            Url = TrendForceUrl,
            ThesisId = dramThesisId,
            Keywords = [.. DramKeywords],
        }, ct);
        return 1;
    }

    private async Task<Guid?> FindDramThesisIdAsync(CancellationToken ct)
    {
        var byTicker = await research.Theses.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(t => t.Ticker == "DRAM")
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (byTicker is not null)
        {
            return byTicker;
        }

        // Fall back to a text match (theses are few) so a DRAM thesis filed under a proxy ticker still binds.
        var candidates = await research.Theses.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(t => new { t.Id, t.ThesisText })
            .ToListAsync(ct);
        return candidates
            .FirstOrDefault(t => t.ThesisText.Contains("DRAM", StringComparison.OrdinalIgnoreCase))?.Id;
    }
}

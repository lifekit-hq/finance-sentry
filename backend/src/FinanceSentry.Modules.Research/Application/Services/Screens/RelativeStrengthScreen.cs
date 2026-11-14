namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using System.Globalization;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Domain.Screens;
using Microsoft.Extensions.Options;

/// <summary>
/// The relative-strength screen (discovery funnel stage 5): the reference-list names Radar already keeps bars for are
/// ranked among themselves by the opportunity scan's own nomination rules (<see cref="ScanNominationRules"/>, thresholds
/// from <see cref="OpportunityOptions"/>), so the percentile means "strong among the index and theme names" and not
/// "strong among whatever the book happens to hold". Reads stored bars only: no provider call, no model.
/// </summary>
public sealed class RelativeStrengthScreen(
    ScreenUniverse universe,
    IMarketStructureReader structure,
    IOptions<ScreenOptions> options,
    IOptions<OpportunityOptions> opportunity) : IScreen
{
    public string Tag => ScreenTags.RelativeStrength;

    public bool Enabled => options.Value.RelativeStrength.Enabled;

    public int MaxNominationsPerRun => options.Value.RelativeStrength.MaxNominationsPerRun;

    public async Task<ScreenRun> RunAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var settings = options.Value.RelativeStrength;
        var referenced = (await universe.TickersAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var pool = (await structure.GetUniverseStructuresAsync(ct))
            .Where(e => ReferenceListKeys.NormalizeTicker(e.Ticker) is { } ticker && referenced.Contains(ticker) && !e.IsEtfLens && !e.Snapshot.Stale)
            .ToList();
        if (pool.Count < settings.MinPoolSize)
        {
            return new ScreenRun(
                Tag, [], pool.Count,
                string.Create(CultureInfo.InvariantCulture,
                    $"pool too small: {pool.Count} fresh reference-list names have bars, the screen needs {settings.MinPoolSize}"));
        }

        var hits = ScanNominationRules.Evaluate(pool, opportunity.Value)
            .Select(n => new ScreenHit(
                Tag,
                n.Ticker.Trim().ToUpperInvariant(),
                n.Reasons,
                string.Create(CultureInfo.InvariantCulture,
                    $"Relative strength percentile {n.RsPercentile?.ToString("0", CultureInfo.InvariantCulture) ?? "n/a"} among {pool.Count} fresh reference-list names with bars ({string.Join("; ", n.Reasons)})."),
                // Bars change every day, so the data has no evidence date of its own: the run time stands for it, and
                // after a coverage removal the cooldown is what holds the name back.
                asOf))
            .ToList();

        return new ScreenRun(Tag, hits, pool.Count, null);
    }
}

namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using System.Globalization;
using FinanceSentry.Modules.Research.Domain.Screens;
using Microsoft.Extensions.Options;

/// <summary>
/// The dated-catalyst screen (discovery funnel stage 5): reference-list names reporting earnings within
/// <c>LookaheadDays</c>, from Finnhub's market-wide earnings calendar (one request a run, far inside the free tier's 60 a
/// minute, keyed by <c>AnalystSources:Finnhub</c>). A forward, dated event is a catalyst that has not happened yet, which
/// is why it is worth a screen of its own beside the price-and-attention one. No model call.
/// </summary>
public sealed class CatalystScreen(
    ScreenUniverse universe,
    IEarningsWindowSource calendar,
    IOptions<ScreenOptions> options) : IScreen
{
    public string Tag => ScreenTags.Catalyst;

    public bool Enabled => options.Value.Catalyst.Enabled;

    public int MaxNominationsPerRun => options.Value.Catalyst.MaxNominationsPerRun;

    public async Task<ScreenRun> RunAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        if (!calendar.IsConfigured)
        {
            return ScreenRun.Skipped(Tag, "no Finnhub API key is configured (AnalystSources:Finnhub:ApiKey)");
        }

        var settings = options.Value.Catalyst;
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        var referenced = (await universe.TickersAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var items = await calendar.GetWindowAsync(today, today.AddDays(settings.LookaheadDays), ct);

        var hits = CatalystScreenRules.Select(items, referenced, today, settings.LookaheadDays)
            .Select(c => new ScreenHit(
                Tag,
                c.Ticker,
                [],
                string.Create(CultureInfo.InvariantCulture,
                    $"Reports earnings on {c.EarningsDate:yyyy-MM-dd}, {c.EarningsDate.DayNumber - today.DayNumber} day(s) ahead."),
                // The catalyst came into view when its date entered the lookahead window: a removal after that was made
                // with this catalyst already in sight.
                new DateTimeOffset(c.EarningsDate.AddDays(-settings.LookaheadDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)))
            .ToList();

        return new ScreenRun(Tag, hits, items.Count, null);
    }
}

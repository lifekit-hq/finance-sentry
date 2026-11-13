namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one attention refresh did.</summary>
public sealed record AttentionRefreshSummary(bool Ran, int Themes, int Phrases, int DaysWritten, int Failed, int Unmeasured)
{
    public static AttentionRefreshSummary Disabled { get; } = new(false, 0, 0, 0, 0, 0);
}

/// <summary>
/// Stores GDELT article volume per theme phrase (discovery funnel stage 7). The themes are the reference universe's (the stage 3
/// lists, plus any theme declared in <c>ReferenceUniverse:Themes</c> that the weekly refresh has not materialised yet); the words
/// that mean each theme in the press are <see cref="CatalystOptions.ThemePhrases"/>. Requests are spaced to GDELT's one per five
/// seconds. A phrase GDELT will not answer is counted and logged and the others are still stored; the refresh then throws, so the
/// job ends Failed instead of leaving a stale measure looking current. No model is called.
/// </summary>
public sealed class ThemeAttentionRefresher(
    IReferenceUniverseReader reference,
    IGdeltTimelineClient gdelt,
    IThemeAttentionRepository repository,
    IOptions<CatalystOptions> options,
    IOptions<ReferenceUniverseOptions> referenceOptions,
    TimeProvider clock,
    ILogger<ThemeAttentionRefresher> logger)
{
    private const string Source = "gdelt";

    private readonly CatalystOptions _options = options.Value;

    public async Task<AttentionRefreshSummary> RefreshAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled || !_options.AttentionEnabled)
        {
            logger.LogInformation("Theme attention refresh is off (Catalyst:Enabled or Catalyst:AttentionEnabled is false)");
            return AttentionRefreshSummary.Disabled;
        }

        var now = clock.GetUtcNow();
        var themes = (await reference.GetListsAsync(ct))
            .Where(l => l.Kind == "theme")
            .Select(l => l.ListKey)
            .Concat(referenceOptions.Value.Themes.Keys.Select(ReferenceListKeys.Theme))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var phrasesByTheme = _options.ThemePhrases
            .ToDictionary(p => ReferenceListKeys.Theme(p.Key), p => p.Value, StringComparer.Ordinal);

        int phrases = 0, written = 0, failed = 0, unmeasured = 0;
        foreach (var theme in themes)
        {
            if (!phrasesByTheme.TryGetValue(theme, out var words) || words.Count == 0)
            {
                unmeasured++;
                logger.LogWarning("Theme attention: {Theme} has no phrase in Catalyst:ThemePhrases, so it is not measured", theme);
                continue;
            }

            foreach (var phrase in words.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                phrases++;
                try
                {
                    var volume = await gdelt.GetDailyVolumeAsync(phrase, _options.GdeltTimespanDays, ct);
                    written += await repository.UpsertDaysAsync(
                        [.. volume.Select(d => new ThemeAttentionDay
                        {
                            Theme = theme,
                            Phrase = phrase,
                            Day = d.Day,
                            Articles = d.Articles,
                            TotalArticles = d.TotalArticles,
                            Source = Source,
                            MeasuredAt = now,
                        })],
                        ct);
                }
                catch (CatalystFeedException ex)
                {
                    failed++;
                    logger.LogError(ex, "Theme attention: the volume of \"{Phrase}\" ({Theme}) could not be read", phrase, theme);
                }
            }
        }

        await repository.PruneAsync(DateOnly.FromDateTime(now.UtcDateTime).AddDays(-_options.AttentionRetentionDays), ct);
        logger.LogInformation(
            "Theme attention: {Themes} theme(s), {Phrases} phrase(s) asked, {Days} day(s) written, {Failed} failed, {Unmeasured} theme(s) without a phrase",
            themes.Count, phrases, written, failed, unmeasured);

        if (failed > 0)
        {
            throw new InvalidOperationException(
                $"Theme attention could not read {failed} of {phrases} phrase(s); the others were stored.");
        }

        return new AttentionRefreshSummary(true, themes.Count, phrases, written, failed, unmeasured);
    }
}

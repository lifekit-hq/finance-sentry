namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// What one attention refresh did. <see cref="Note"/> says why a run that stored nothing is not a failure (switched off, or no
/// phrase to ask) and, for a run that stored some phrases but not all, that the rest were throttled.
/// </summary>
public sealed record AttentionRefreshSummary(bool Ran, int Themes, int Phrases, int DaysWritten, int Failed, int Unmeasured, string? Note = null)
{
    public static AttentionRefreshSummary Disabled { get; } = new(false, 0, 0, 0, 0, 0, "Catalyst:Enabled or Catalyst:AttentionEnabled is false");
}

/// <summary>An attention refresh that failed, naming the step it failed in so a job-failure alert can be read from the log.</summary>
public sealed class ThemeAttentionRefreshException(string step, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public string Step { get; } = step;
}

/// <summary>
/// Stores GDELT article volume per theme phrase (discovery funnel stage 7). The themes are the reference universe's (the stage 3
/// lists, plus any theme declared in <c>ReferenceUniverse:Themes</c> that the weekly refresh has not materialised yet); the words
/// that mean each theme in the press are <see cref="CatalystOptions.ThemePhrases"/>. Requests are spaced to GDELT's one per five
/// seconds. A phrase GDELT will not answer is counted and logged and the others are still stored. GDELT throttles this host's egress
/// for days at a time, so a run that stored some phrases ends with a partial summary (the failed count and a warning) rather than
/// failing the daily job; a run that measured none of the phrases it asked for throws a <see cref="ThemeAttentionRefreshException"/>
/// naming the step, so the job ends Failed instead of leaving a stale measure looking current. No model is called.
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

        var step = "read-themes";
        try
        {
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
                    IReadOnlyList<GdeltDay> volume;
                    step = "fetch-volume";
                    try
                    {
                        volume = await gdelt.GetDailyVolumeAsync(phrase, _options.GdeltTimespanDays, ct);
                    }
                    catch (CatalystFeedException ex)
                    {
                        failed++;
                        logger.LogError(ex, "Theme attention: the volume of \"{Phrase}\" ({Theme}) could not be read", phrase, theme);
                        continue;
                    }

                    step = "store-volume";
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
            }

            step = "prune";
            await repository.PruneAsync(DateOnly.FromDateTime(now.UtcDateTime).AddDays(-_options.AttentionRetentionDays), ct);
            logger.LogInformation(
                "Theme attention: {Themes} theme(s), {Phrases} phrase(s) asked, {Days} day(s) written, {Failed} failed, {Unmeasured} theme(s) without a phrase",
                themes.Count, phrases, written, failed, unmeasured);

            if (phrases == 0)
            {
                const string NoInput = "no theme has a phrase in Catalyst:ThemePhrases, so there is nothing to measure";
                logger.LogWarning("Theme attention refresh skipped: {Reason}", NoInput);
                return new AttentionRefreshSummary(true, themes.Count, 0, 0, 0, unmeasured, NoInput);
            }

            if (failed >= phrases)
            {
                step = "fetch-volume";
                throw new ThemeAttentionRefreshException(
                    step, $"Theme attention could not read any of {phrases} phrase(s); nothing was stored.");
            }

            if (failed > 0)
            {
                var partial = $"{failed} of {phrases} phrase(s) were not answered (GDELT throttling); their last stored readings are kept";
                logger.LogWarning("Theme attention refresh partial: {Reason}", partial);
                return new AttentionRefreshSummary(true, themes.Count, phrases, written, failed, unmeasured, partial);
            }

            return new AttentionRefreshSummary(true, themes.Count, phrases, written, failed, unmeasured);
        }
        catch (Exception ex) when (ex is not ThemeAttentionRefreshException and not OperationCanceledException)
        {
            throw new ThemeAttentionRefreshException(step, $"Theme attention failed in step {step}: {ex.Message}", ex);
        }
    }
}

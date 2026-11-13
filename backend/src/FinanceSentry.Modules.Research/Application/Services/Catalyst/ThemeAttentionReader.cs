namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>Reads the stored GDELT attention measure per theme phrase and the facts a candidate carries from it.</summary>
public interface IThemeAttentionReader
{
    /// <summary>One reading per phrase of the theme (id <c>memory</c> or key <c>theme:memory</c>) or, with null, of every theme; empty when nothing is stored.</summary>
    Task<IReadOnlyList<ThemeAttentionReading>> GetReadingsAsync(string? theme, CancellationToken ct = default);

    /// <summary>The readings of every theme the ticker belongs to, with a note when there are none to give.</summary>
    Task<AttentionFact> GetForTickerAsync(string ticker, CancellationToken ct = default);
}

public sealed class ThemeAttentionReader(
    IThemeAttentionRepository repository,
    IReferenceUniverseReader reference,
    IOptions<CatalystOptions> options,
    TimeProvider clock) : IThemeAttentionReader
{
    private readonly CatalystOptions _options = options.Value;

    public async Task<IReadOnlyList<ThemeAttentionReading>> GetReadingsAsync(string? theme, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var days = await repository.ListDaysAsync(NormaliseTheme(theme), today.AddDays(-_options.AttentionRetentionDays), ct);
        return Readings(days, today);
    }

    public async Task<AttentionFact> GetForTickerAsync(string ticker, CancellationToken ct = default)
    {
        var themes = (await reference.GetListsForTickerAsync(ticker, ct))
            .Where(k => k.StartsWith(ReferenceListKeys.ThemePrefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        if (themes.Count == 0)
        {
            return AttentionFact.None("The ticker is on no reference theme list.");
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var days = (await repository.ListDaysAsync(null, today.AddDays(-_options.AttentionRetentionDays), ct))
            .Where(d => themes.Contains(d.Theme))
            .ToList();
        var readings = Readings(days, today);
        return readings.Count == 0
            ? AttentionFact.None("No attention reading is stored yet for the ticker's themes.")
            : new AttentionFact(readings);
    }

    private IReadOnlyList<ThemeAttentionReading> Readings(IReadOnlyList<ThemeAttentionDay> days, DateOnly today)
        => [.. days
            .GroupBy(d => (d.Theme, d.Phrase))
            .OrderBy(g => g.Key.Theme, StringComparer.Ordinal).ThenBy(g => g.Key.Phrase, StringComparer.Ordinal)
            .Select(g => AttentionMath.Reading(
                [.. g], today, _options.AttentionBaselineDays, _options.AttentionMinBaselineDays))
            .OfType<ThemeAttentionReading>()];

    private static string? NormaliseTheme(string? theme)
    {
        var t = theme?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(t))
        {
            return null;
        }

        return t.StartsWith(ReferenceListKeys.ThemePrefix, StringComparison.Ordinal) ? t : ReferenceListKeys.Theme(t);
    }
}

namespace FinanceSentry.Modules.Alerts.Application.Services;

using FinanceSentry.Modules.Alerts.Domain;

/// <summary>
/// Works out, from the alerts already raised for one drift (finance-sentry#1266), whether the drift the daily run
/// sees now deserves another notification. An episode runs from the moment the drift last cleared; it keeps a
/// high-water mark per <c>Asset:Direction</c> of the band multiple it announced, and only a drift that is new to the
/// episode or further out than that mark is worth a new alert. Shrinking, or flickering around an integer multiple,
/// is not. The state is the alert rows themselves: no stored counter.
/// </summary>
internal static class DriftEpisode
{
    private const char SleeveSeparator = ';';
    private const char FieldSeparator = ':';
    private const int FieldsPerSleeve = 3;

    /// <param name="rows">Every alert raised for the drift, any state.</param>
    /// <param name="signature">The drift now, as stored in <see cref="Alert.DedupKey"/>: <c>Asset:Direction:BandsOut</c> per sleeve, joined by <c>;</c>.</param>
    /// <param name="cadenceStart">Alerts the operator acted on stop holding the episode once created before this.</param>
    public static bool Worsens(IReadOnlyCollection<Alert> rows, string? signature, DateTimeOffset cadenceStart)
    {
        var marks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in Holding(rows, cadenceStart))
        {
            var announced = Parse(row.DedupKey);
            if (announced.Count == 0)
            {
                // An alert from before proposals carried a drift key: unreadable, so taken as this same drift.
                return false;
            }

            foreach (var (key, level) in announced)
            {
                marks[key] = marks.TryGetValue(key, out var mark) ? Math.Max(mark, level) : level;
            }
        }

        if (marks.Count == 0) return true;
        return Parse(signature).Any(s => !marks.TryGetValue(s.Key, out var mark) || s.Value > mark);
    }

    /// <summary>The alerts that still speak for the current episode, newest first.</summary>
    private static List<Alert> Holding(IReadOnlyCollection<Alert> rows, DateTimeOffset cadenceStart)
    {
        var newestFirst = rows.OrderByDescending(a => a.CreatedAt).ToList();

        DateTimeOffset? lastClear = null;
        for (var i = 0; i < newestFirst.Count; i++)
        {
            var successor = i > 0 ? newestFirst[i - 1] : null;
            if (IsCleared(newestFirst[i], successor) && (lastClear is null || newestFirst[i].ResolvedAt > lastClear))
            {
                lastClear = newestFirst[i].ResolvedAt;
            }
        }

        var holding = new List<Alert>();
        var successorHolds = false;
        for (var i = 0; i < newestFirst.Count; i++)
        {
            var row = newestFirst[i];
            if (lastClear is not null && row.CreatedAt <= lastClear) break;

            var successor = i > 0 ? newestFirst[i - 1] : null;
            successorHolds = IsOpenUndecided(row)
                || row.CreatedAt >= cadenceStart
                || (IsSupersededBy(row, successor) && successorHolds);
            if (successorHolds) holding.Add(row);
        }

        return holding;
    }

    private static bool IsOpenUndecided(Alert row)
        => !row.IsResolved && !row.IsDismissed && row.AcknowledgementDecision is null;

    private static bool IsSupersededBy(Alert row, Alert? successor)
        => row.ResolvedAt is { } at && successor is not null && at == successor.CreatedAt;

    /// <summary>
    /// Resolved by the system with the drift gone: an open proposal nobody acted on, not a worsening's supersede.
    /// One the operator accepted, deferred or dismissed ages out on the cadence instead.
    /// </summary>
    private static bool IsCleared(Alert row, Alert? successor)
        => row is { IsResolved: true, IsDismissed: false, AcknowledgementDecision: null, ResolvedAt: not null }
           && !IsSupersededBy(row, successor);

    private static Dictionary<string, int> Parse(string? signature)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sleeve in (signature ?? string.Empty).Split(SleeveSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = sleeve.Split(FieldSeparator);
            if (fields.Length != FieldsPerSleeve || !int.TryParse(fields[2], out var level)) continue;

            var key = $"{fields[0]}{FieldSeparator}{fields[1]}";
            levels[key] = levels.TryGetValue(key, out var mark) ? Math.Max(mark, level) : level;
        }

        return levels;
    }
}

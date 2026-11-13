namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

/// <summary>
/// Spreads a list too long for one run over several: run <c>n</c> reads slice <c>n mod slices</c>, so every name is read once
/// every <c>slices</c> runs and no name is starved. Stateless on purpose - the run index comes from the clock - so a restart
/// or a skipped run only shifts which slice is next.
/// </summary>
public static class TickerRotation
{
    /// <summary>The run index of <paramref name="at"/> for runs every <paramref name="periodMinutes"/> minutes.</summary>
    public static long RunIndex(DateTimeOffset at, int periodMinutes)
        => at.ToUnixTimeSeconds() / (Math.Max(1, periodMinutes) * 60L);

    /// <summary>
    /// The slice of <paramref name="ordered"/> that run <paramref name="runIndex"/> reads, at most <paramref name="perRun"/> long.
    /// A list that fits in one run is returned whole; <paramref name="perRun"/> of zero or less reads nothing.
    /// </summary>
    public static IReadOnlyList<string> Slice(IReadOnlyList<string> ordered, int perRun, long runIndex)
    {
        if (perRun <= 0 || ordered.Count == 0)
        {
            return [];
        }

        if (ordered.Count <= perRun)
        {
            return ordered;
        }

        var slices = (ordered.Count + perRun - 1) / perRun;
        var slice = (int)(runIndex % slices);
        return [.. ordered.Skip(slice * perRun).Take(perRun)];
    }
}

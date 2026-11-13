namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using FinanceSentry.Modules.Research.Domain.Screens;

/// <summary>What one screen found in one run: its hits best-first, how many names it looked at, and a note when it could not look.</summary>
public sealed record ScreenRun(string Tag, IReadOnlyList<ScreenHit> Hits, int Examined, string? Note)
{
    public static ScreenRun Skipped(string tag, string note) => new(tag, [], 0, note);
}

/// <summary>
/// One deterministic screen over the reference universe. A screen reads shared market data only (never a user's), so a
/// run's hits are the same for every user; the nominator applies the per-user rules (coverage, cooldown, caps).
/// </summary>
public interface IScreen
{
    /// <summary>The tag the screen's nominations lead with (<see cref="ScreenTags"/>).</summary>
    string Tag { get; }

    bool Enabled { get; }

    /// <summary>Most names the screen nominates for one user in one run.</summary>
    int MaxNominationsPerRun { get; }

    /// <summary>Runs the screen; hits are ordered best first. Throws when the screen could not run, so the run fails instead of reading as "nothing found".</summary>
    Task<ScreenRun> RunAsync(DateTimeOffset asOf, CancellationToken ct = default);
}

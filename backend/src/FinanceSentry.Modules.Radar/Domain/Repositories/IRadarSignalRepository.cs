namespace FinanceSentry.Modules.Radar.Domain.Repositories;

using FinanceSentry.Modules.Radar.Domain;

public sealed record SignalFilter(
    DateTimeOffset? Since = null,
    string? Scanner = null,
    string? SignalType = null,
    string? Subject = null,
    Guid? UserId = null,
    string? Severity = null);

public interface IRadarSignalRepository
{
    Task AppendAsync(RadarSignal signal, CancellationToken ct = default);

    /// <summary>
    /// True if a signal with this DedupKey exists at/after <paramref name="since"/> (silence window), whoever holds
    /// it. The writer runs from jobs with no person in scope, so it opts out of the Owner query filter.
    /// </summary>
    Task<bool> HasRecentUnscopedAsync(string dedupKey, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Signals matching <paramref name="filter"/> that the acting person may see: global ones plus their own.</summary>
    Task<IReadOnlyList<RadarSignal>> ListAsync(SignalFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Global signals plus <paramref name="userId"/>'s own, for jobs with no person in scope. Opts out of the Owner
    /// query filter; <paramref name="userId"/> replaces any <see cref="SignalFilter.UserId"/>.
    /// </summary>
    Task<IReadOnlyList<RadarSignal>> ListForUserUnscopedAsync(
        Guid userId, SignalFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Prunes every holder's <c>info</c> signals older than <paramref name="cutoff"/>. Returns rows removed. The
    /// retention job has no person in scope, so it opts out of the Owner query filter.
    /// </summary>
    Task<int> PruneInfoBeforeUnscopedAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}

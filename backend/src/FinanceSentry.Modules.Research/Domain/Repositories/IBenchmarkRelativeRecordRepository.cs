namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface IBenchmarkRelativeRecordRepository
{
    /// <summary>
    /// Writes one run's rows, replacing any rows an earlier attempt of the same run stored. The snapshot job writes with
    /// no person in scope, so the lookup of earlier rows opts out of the Owner query filter.
    /// </summary>
    Task ReplaceRunAsync(
        Guid userId, DateTimeOffset asOf, IReadOnlyList<BenchmarkRelativeRecord> rows, CancellationToken ct = default);

    /// <summary>
    /// Rows of the most recent run strictly before <paramref name="asOf"/> (empty when none), for the snapshot job.
    /// Opts out of the Owner query filter.
    /// </summary>
    Task<IReadOnlyList<BenchmarkRelativeRecord>> ListPreviousRunUnscopedAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>Rows of the most recent run (empty when the job has not run yet).</summary>
    Task<IReadOnlyList<BenchmarkRelativeRecord>> ListLatestRunAsync(Guid userId, CancellationToken ct = default);
}

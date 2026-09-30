namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface IBenchmarkRelativeRecordRepository
{
    /// <summary>Writes one run's rows, replacing any rows an earlier attempt of the same run stored.</summary>
    Task ReplaceRunAsync(
        Guid userId, DateTimeOffset asOf, IReadOnlyList<BenchmarkRelativeRecord> rows, CancellationToken ct = default);

    /// <summary>Rows of the most recent run strictly before <paramref name="asOf"/> (empty when none).</summary>
    Task<IReadOnlyList<BenchmarkRelativeRecord>> ListPreviousRunAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>Rows of the most recent run (empty when the job has not run yet).</summary>
    Task<IReadOnlyList<BenchmarkRelativeRecord>> ListLatestRunAsync(Guid userId, CancellationToken ct = default);
}

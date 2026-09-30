namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The level a <see cref="BenchmarkRelativeRecord"/> summarises: the whole monitored thesis book,
/// one asset-class sleeve of it, or a single thesis.
/// </summary>
public enum TrackRecordScope
{
    Book,
    Sleeve,
    Thesis,
}

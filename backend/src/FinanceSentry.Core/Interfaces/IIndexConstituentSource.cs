namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Supplies the broad-market index constituent tickers (checked-in seed list). Cross-module port so
/// consumers outside the owning module can widen their universe without duplicating the list.
/// </summary>
public interface IIndexConstituentSource
{
    /// <summary>Constituent tickers, upper-cased and de-duplicated.</summary>
    IReadOnlyList<string> GetConstituents();
}

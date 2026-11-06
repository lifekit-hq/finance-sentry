namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Published read port (#454 S7): a one-glance summary of the Irish disposal-tax context across the person's held
/// positions (#454 S6), for cross-module readers such as Wealth. Implemented inside Research, so Wealth never references
/// it. Computed on read, nothing stored. Callers run with the person in scope.
/// </summary>
public interface IDisposalTaxSummaryReader
{
    Task<DisposalTaxSummary> ReadAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// How many of the held positions have a disposal-tax estimate and how many are withheld, and why.
/// <paramref name="EstimatedTaxEur"/> adds the shown positions' estimates before the CGT annual exemption (which the
/// year's disposals share), as if each were sold today; null when none is shown. <paramref name="WithheldReasons"/> counts
/// the typed reason codes across the withheld positions (a position can carry several).
/// </summary>
public sealed record DisposalTaxSummary(
    int PositionCount,
    int ShownCount,
    int WithheldCount,
    decimal? EstimatedTaxEur,
    int? TaxYear,
    IReadOnlyDictionary<string, int> WithheldReasons);

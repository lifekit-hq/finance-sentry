namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// The statement history a DCF is built from (#1269), from one provider and one basis so that no ratio divides
/// one provider's figure by another's. Annual periods only, newest first; the cash-flow lines keep the provider's
/// sign convention (capex and a working-capital outflow negative), which the DCF query normalizes.
/// </summary>
public interface IDcfInputsSource
{
    /// <summary>The ticker's statement history, or <c>null</c> when the provider has none or could not be read.</summary>
    Task<DcfInputs?> GetAsync(string ticker, CancellationToken ct = default);
}

/// <summary>
/// <see cref="Annual"/> is newest first. <see cref="TtmRevenue"/> is the sum of the four newest consecutive
/// quarters (null unless all four are present) and <see cref="LatestQuarterEnd"/> the end of the newest one: the
/// base year when the latest annual period is stale. Every figure is nullable: a missing line is null, never zero.
/// </summary>
public sealed record DcfInputs(
    IReadOnlyList<DcfAnnualPeriod> Annual,
    decimal? TtmRevenue,
    DateOnly? LatestQuarterEnd);

/// <summary>One fiscal year's lines, in the reporting currency. Interest expense and tax provision are positive when paid.</summary>
public sealed record DcfAnnualPeriod(
    DateOnly PeriodEnd,
    decimal? Revenue,
    decimal? OperatingIncome,
    decimal? TaxProvision,
    decimal? PretaxIncome,
    decimal? InterestExpense,
    decimal? DepreciationAndAmortization,
    decimal? CapitalExpenditure,
    decimal? ChangeInWorkingCapital);

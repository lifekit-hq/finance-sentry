namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Cross-module read port over BankSync's counterparty- and transfer-corrected monthly cash
/// flow. Wealth consumes this instead of referencing BankSync directly, so the FIRE calculator
/// reads the same honest savings rate the dashboard renders.
/// </summary>
public interface IHonestMonthlyFlowReader
{
    /// <summary>
    /// Returns one row per calendar month over the trailing <paramref name="months"/> complete
    /// months plus the in-progress one, each already summed across every currency the user
    /// holds (native amounts do not cross this boundary — see the currency-aggregation rule).
    /// </summary>
    Task<IReadOnlyList<HonestMonthlyFlow>> GetMonthlyFlowAsync(
        Guid userId, int months, CancellationToken ct = default);
}

/// <summary>One calendar month's honest outflow and net savings, USD, "yyyy-MM" keyed.</summary>
public sealed record HonestMonthlyFlow(string Month, decimal OutflowUsd, decimal NetUsd);

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

    /// <summary>
    /// The same honest net (inflow less outflow, counterparty and transfer corrected, USD) over the UTC days
    /// <paramref name="from"/> through <paramref name="to"/>, both inclusive: the savings line of the net-worth
    /// bridge (M5) for a window that is not a whole month. An account counts only from its link day to its removal day: its
    /// earlier imported history and any later transactions reach the bridge through its newly linked / unlinked line.
    /// </summary>
    Task<decimal> GetWindowNetAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>One calendar month's honest outflow and net savings, USD, "yyyy-MM" keyed.</summary>
public sealed record HonestMonthlyFlow(string Month, decimal OutflowUsd, decimal NetUsd);

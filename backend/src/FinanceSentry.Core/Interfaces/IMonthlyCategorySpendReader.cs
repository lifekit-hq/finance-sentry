namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Cross-module read port over BankSync's honest spend, bucketed by calendar month and by estimate line (budgets
/// consume it instead of referencing BankSync). It reads the same classification the dashboard's Spending tile does
/// (pending included, pair-matched transfers and investment routing out, counterparty roles applied), so a month's
/// figure here can never disagree with the flow it explains. Names the user explicitly, so it is safe from a job with
/// no principal.
/// </summary>
public interface IMonthlyCategorySpendReader
{
    /// <summary>
    /// Spend by (month, line) for the months from <paramref name="from"/> through <paramref name="to"/>, every
    /// amount in USD (converted per account currency at the read boundary, never a native sum), plus the active
    /// commitments placed on the line their charges land in.
    /// </summary>
    Task<MonthlyCategorySpend> GetAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// The lines an estimate is made of: a category key, or one of the two counterparty-role lines that have no category
/// of their own (family support is a canonical key; household bills, such as a mortgage paid card to card, are not).
/// </summary>
public static class SpendLines
{
    /// <summary>The household-role outflow: a standing bill paid as a transfer (money-semantics §5.1).</summary>
    public const string Household = "HOUSEHOLD";
}

/// <summary>One line's spend in one calendar month, USD.</summary>
/// <param name="Month">"yyyy-MM".</param>
public sealed record CategoryMonthSpend(string Month, string Line, decimal SpendUsd);

/// <summary>An active commitment's monthly amount, USD, on the line its charges land in.</summary>
/// <param name="EndDate">Final payment month, when known; the commitment stops counting after it.</param>
public sealed record CommitmentOnLine(string Line, decimal MonthlyUsd, DateOnly? EndDate);

/// <param name="EndedCommitments">
/// Commitments that stopped (a paid-off installment, a lapsed rent), placed on their line like
/// <paramref name="Commitments"/>: they tell a line whose commitment ended from one that never had any.
/// </param>
/// <param name="FirstDataDay">
/// The day of the user's earliest transaction of any kind, or null with no history. A month before it is "no data",
/// not a month of zero spend.
/// </param>
public sealed record MonthlyCategorySpend(
    DateOnly? FirstDataDay,
    IReadOnlyList<CategoryMonthSpend> Months,
    IReadOnlyList<CommitmentOnLine> Commitments,
    IReadOnlyList<CommitmentOnLine> EndedCommitments);

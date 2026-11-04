namespace FinanceSentry.Modules.Budgets.Application.Estimates;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Domain;

/// <summary>Which lines an estimate is made of and which of them are standing obligations.</summary>
public static class EstimateLines
{
    /// <summary>
    /// Lines whose spend is a standing obligation, the same set the committed-versus-discretionary split reads
    /// (<c>CommittedOutflowRules</c>: committed categories and committed counterparty roles). A detected commitment
    /// is the estimate of such a line, and its month-end expectation is what is still due, never a pace projection (a
    /// rent paid on the 1st would project to thirty times itself).
    /// </summary>
    private static readonly HashSet<string> Committed = new(StringComparer.Ordinal)
    {
        CategoryKeys.LoanPayments,
        CategoryKeys.RentAndUtilities,
        CategoryKeys.FamilySupport,
        SpendLines.Household,
    };

    // Not spending, so never a line: income, and money moving between the person's own accounts.
    private static readonly HashSet<string> NotSpend = new(StringComparer.Ordinal)
    {
        CategoryKeys.Income,
        CategoryKeys.TransferIn,
        CategoryKeys.TransferOut,
    };

    public static bool IsCommitted(string line) => Committed.Contains(line);

    /// <summary>Whether <paramref name="line"/> is one an estimate (and so an override) can exist for.</summary>
    public static bool IsEstimable(string line)
        => line == SpendLines.Household
           || (CategoryTaxonomy.ValidKeys.Contains(line) && !NotSpend.Contains(line));
}

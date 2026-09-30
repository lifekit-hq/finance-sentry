namespace FinanceSentry.Modules.Research.Domain.PolicyReviews;

/// <summary>
/// Reads the review cadence recorded on the investment policy statement (#696) as a whole number
/// of months. The statement stores the cadence as free text (default <c>annual</c>), so the common
/// spellings map here; anything unrecognised falls back to annual — the statement's own default —
/// and is flagged so the review can say it assumed one.
/// </summary>
public static class PolicyReviewCadence
{
    public const int DefaultMonths = 12;

    private static readonly Dictionary<string, int> MonthsByCadence = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monthly"] = 1,
        ["quarterly"] = 3,
        ["semiannual"] = 6,
        ["semi-annual"] = 6,
        ["semiannually"] = 6,
        ["semi-annually"] = 6,
        ["biannual"] = 6,
        ["half-yearly"] = 6,
        ["annual"] = 12,
        ["annually"] = 12,
        ["yearly"] = 12,
    };

    /// <summary>The cadence in months, or false (with the annual default) when it is not recognised.</summary>
    public static bool TryGetMonths(string? cadence, out int months)
    {
        if (cadence is not null && MonthsByCadence.TryGetValue(cadence.Trim(), out months))
            return true;

        months = DefaultMonths;
        return false;
    }
}

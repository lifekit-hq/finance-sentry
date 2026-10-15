namespace FinanceSentry.Core.Headlines;

/// <summary>
/// The scope strings a <see cref="Headline"/> names. <see cref="Umbrella"/>, <see cref="InvestedBrokerage"/>,
/// <see cref="Source"/> and <see cref="Account"/> are the framework's (§3.6); <see cref="Sleeve"/> covers the
/// per-sleeve totals (banking, brokerage, crypto) and the cash/invested split lines.
/// </summary>
public static class HeadlineScope
{
    /// <summary>Everything the person owns and owes: total net worth.</summary>
    public const string Umbrella = "umbrella";

    /// <summary>The brokerage sleeve without its idle cash: what the book-vs-SPY figure measures.</summary>
    public const string InvestedBrokerage = "invested_brokerage";

    public const string CashSleeve = "sleeve:cash";
    public const string InvestedSleeve = "sleeve:invested";

    public static string Source(string provider) => $"source:{provider.ToLowerInvariant()}";

    public static string Account(Guid accountId) => $"account:{accountId:N}";

    public static string Sleeve(string name) => $"sleeve:{name.ToLowerInvariant()}";
}

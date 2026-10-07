namespace FinanceSentry.Core.Utils;

/// <summary>How old a brokerage provider's holdings may get before views call them stale (money-semantics §1).</summary>
public static class BrokerageFreshness
{
    /// <summary>Holdings refreshed through the day (IBKR's 15-minute sync).</summary>
    public static readonly TimeSpan Intraday = TimeSpan.FromHours(1);

    /// <summary>Holdings read once a day (Inzhur); matches the net-worth snapshot's 36 h window.</summary>
    public static readonly TimeSpan Daily = TimeSpan.FromHours(36);

    private static readonly HashSet<string> DailyProviders = new(StringComparer.OrdinalIgnoreCase) { "inzhur" };

    public static TimeSpan StaleAfter(string? provider)
        => provider is not null && DailyProviders.Contains(provider) ? Daily : Intraday;

    public static bool IsStale(string? provider, DateTime syncedAtUtc, DateTime nowUtc)
        => nowUtc - syncedAtUtc > StaleAfter(provider);
}

namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;

/// <summary>Synthetic scorecards for the tests: no real company data, only the shapes the rules read.</summary>
internal static class Cards
{
    public static readonly DateTimeOffset ScoredAt = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    public static UniverseScorecard Scored(
        string symbol,
        string word = "Buy",
        int? structure = 70,
        int? fundamentals = 70,
        string? crowding = "Normal",
        bool stale = false,
        bool notApplicable = false,
        string? sector = null,
        string? industry = null) =>
        new(symbol, null, ScoredAt, structure, structure, "adjusted", [], fundamentals, [], crowding, 0.04m,
            notApplicable, 28m, 24m, 16.7m, 9.1m, null, stale, word, 1, "buy", "Adjusted structure at least 50 and fundamentals at least 50.", sector, industry);
}

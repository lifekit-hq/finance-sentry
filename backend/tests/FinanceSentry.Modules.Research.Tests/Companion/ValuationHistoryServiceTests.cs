namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Trailing-P/E history math (feature 030, T031): TTM EPS from EDGAR diluted-EPS quarters ÷ Yahoo
/// daily closes. Short-history IPOs report their actual window; missing data → null, never zero.
/// </summary>
public sealed class ValuationHistoryServiceTests
{
    private static FundamentalFact Eps(DateOnly periodEnd, decimal value, string quarter) => new(
        "TEST", "DilutedEPS", "Diluted EPS", "USD/shares", value, periodEnd, quarter, periodEnd.Year, "10-Q");

    private static ValuationHistoryService Build(
        IReadOnlyList<FundamentalFact> facts, IReadOnlyList<DailyClose> closes) => new(
        new FakeFundamentalsService(facts),
        new FakeMarketDataService(closes),
        NullLogger<ValuationHistoryService>.Instance);

    [Fact]
    public async Task Rolls_four_quarters_to_ttm_and_prices_against_closes()
    {
        // Eight $1.00 quarters → every TTM = $4.00. A flat $80 close → P/E 20 at every point.
        var quarterEnds = new[]
        {
            new DateOnly(2024, 3, 31), new DateOnly(2024, 6, 30), new DateOnly(2024, 9, 30),
            new DateOnly(2024, 12, 31), new DateOnly(2025, 3, 31), new DateOnly(2025, 6, 30),
            new DateOnly(2025, 9, 30), new DateOnly(2025, 12, 31),
        };
        var quarters = new[] { "Q1", "Q2", "Q3", "Q4" };
        var facts = quarterEnds.Select((d, i) => Eps(d, 1.0m, quarters[i % 4])).ToList();
        var closes = quarterEnds.Select(d => new DailyClose(d, 80m)).ToList();

        var result = await Build(facts, closes).GetTrailingPeHistoryAsync("TEST");

        result.FiveYearAvg.Should().Be(20m);
        result.WindowYears.Should().Be(1, "TTM points span 2024-12-31..2025-12-31 = one year");
    }

    [Fact]
    public async Task Averages_varying_pe_points()
    {
        // Four $1.00 quarters → one TTM point ($4.00), plus a fifth quarter → a second TTM point.
        var facts = new[]
        {
            Eps(new DateOnly(2025, 3, 31), 1.0m, "Q1"),
            Eps(new DateOnly(2025, 6, 30), 1.0m, "Q2"),
            Eps(new DateOnly(2025, 9, 30), 1.0m, "Q3"),
            Eps(new DateOnly(2025, 12, 31), 1.0m, "Q4"),
            Eps(new DateOnly(2026, 3, 31), 1.0m, "Q1"),
        };
        var closes = new[]
        {
            new DailyClose(new DateOnly(2025, 12, 31), 40m), // TTM 4 → P/E 10
            new DailyClose(new DateOnly(2026, 3, 31), 120m), // TTM 4 → P/E 30
        };

        var result = await Build(facts, closes).GetTrailingPeHistoryAsync("TEST");

        result.FiveYearAvg.Should().Be(20m, "(10 + 30) / 2");
    }

    [Fact]
    public async Task Returns_null_when_fewer_than_four_quarters()
    {
        var facts = new[]
        {
            Eps(new DateOnly(2025, 3, 31), 1.0m, "Q1"),
            Eps(new DateOnly(2025, 6, 30), 1.0m, "Q2"),
            Eps(new DateOnly(2025, 9, 30), 1.0m, "Q3"),
        };
        var closes = new[] { new DailyClose(new DateOnly(2025, 9, 30), 80m) };

        var result = await Build(facts, closes).GetTrailingPeHistoryAsync("TEST");

        result.FiveYearAvg.Should().BeNull();
        result.WindowYears.Should().BeNull();
    }

    [Fact]
    public async Task Skips_points_where_ttm_is_not_positive()
    {
        // A loss-making trailing year (TTM ≤ 0) must not produce a negative or zero P/E.
        var facts = new[]
        {
            Eps(new DateOnly(2025, 3, 31), -2.0m, "Q1"),
            Eps(new DateOnly(2025, 6, 30), -2.0m, "Q2"),
            Eps(new DateOnly(2025, 9, 30), -1.0m, "Q3"),
            Eps(new DateOnly(2025, 12, 31), 1.0m, "Q4"),
        };
        var closes = new[] { new DailyClose(new DateOnly(2025, 12, 31), 80m) };

        var result = await Build(facts, closes).GetTrailingPeHistoryAsync("TEST");

        result.FiveYearAvg.Should().BeNull("the only TTM point sums to -4.0");
    }

    [Fact]
    public async Task Returns_null_when_no_closes_to_price_against()
    {
        var facts = new[]
        {
            Eps(new DateOnly(2025, 3, 31), 1.0m, "Q1"),
            Eps(new DateOnly(2025, 6, 30), 1.0m, "Q2"),
            Eps(new DateOnly(2025, 9, 30), 1.0m, "Q3"),
            Eps(new DateOnly(2025, 12, 31), 1.0m, "Q4"),
        };

        var result = await Build(facts, []).GetTrailingPeHistoryAsync("TEST");

        result.FiveYearAvg.Should().BeNull();
    }
}

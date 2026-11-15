namespace FinanceSentry.Modules.Research.Tests.Valuation;

using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

/// <summary>
/// External-contract test (constitution-mandated) for the Yahoo fundamentals-timeseries shape the DCF reads (#1269).
/// The fixture keeps the response structure recorded from the live endpoint on 2026-11-15 (series named
/// <c>annual…</c> / <c>quarterly…</c>, <c>reportedValue.raw</c> in scientific notation) with synthetic figures.
/// </summary>
public sealed class YahooDcfInputsSourceTests
{
    private static string Fixture()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Unit", "Fixtures", "Yahoo", "SYNTH-dcf-timeseries-2026-11-15.json"));

    [Fact]
    public void Parse_reads_each_annual_line_newest_first()
    {
        var inputs = YahooDcfInputsSource.Parse(Fixture());

        inputs.Should().NotBeNull();
        inputs!.Annual.Select(p => p.PeriodEnd).Should().Equal(
            new DateOnly(2025, 12, 31), new DateOnly(2024, 12, 31), new DateOnly(2023, 12, 31));
        var latest = inputs.Annual[0];
        latest.Revenue.Should().Be(1_200_000_000m);
        latest.OperatingIncome.Should().Be(240_000_000m);
        latest.TaxProvision.Should().Be(45_000_000m);
        latest.PretaxIncome.Should().Be(210_000_000m);
        latest.InterestExpense.Should().Be(30_000_000m);
        latest.DepreciationAndAmortization.Should().Be(60_000_000m);
        latest.CapitalExpenditure.Should().Be(-72_000_000m, "capex keeps Yahoo's cash-flow sign");
        latest.ChangeInWorkingCapital.Should().Be(-6_450_000m, "scientific notation (-6.45E6) parses exactly");
    }

    [Fact]
    public void Parse_leaves_a_missing_line_null_never_zero()
    {
        var inputs = YahooDcfInputsSource.Parse(Fixture())!;

        // The oldest year carries no working-capital or interest point in the fixture.
        var oldest = inputs.Annual[^1];
        oldest.ChangeInWorkingCapital.Should().BeNull();
        oldest.InterestExpense.Should().BeNull();
        oldest.Revenue.Should().Be(1_000_000_000m);
    }

    [Fact]
    public void Parse_sums_the_four_newest_consecutive_quarters_into_ttm_revenue()
    {
        var inputs = YahooDcfInputsSource.Parse(Fixture())!;

        inputs.TtmRevenue.Should().Be(1_300_000_000m, "Q4 2025 .. Q3 2026: 300 + 310 + 340 + 350 million");
        inputs.LatestQuarterEnd.Should().Be(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void Parse_gives_no_ttm_when_the_newest_quarters_are_not_four_consecutive_ones()
    {
        const string gap = """
        { "timeseries": { "result": [
          { "meta": { "type": ["annualTotalRevenue"] }, "annualTotalRevenue": [
            { "asOfDate": "2025-12-31", "periodType": "12M", "reportedValue": { "raw": 1.0E9 } } ] },
          { "meta": { "type": ["quarterlyTotalRevenue"] }, "quarterlyTotalRevenue": [
            { "asOfDate": "2025-03-31", "periodType": "3M", "reportedValue": { "raw": 2.0E8 } },
            { "asOfDate": "2025-12-31", "periodType": "3M", "reportedValue": { "raw": 2.0E8 } },
            { "asOfDate": "2026-03-31", "periodType": "3M", "reportedValue": { "raw": 2.0E8 } },
            { "asOfDate": "2026-06-30", "periodType": "3M", "reportedValue": { "raw": 2.0E8 } } ] }
        ] } }
        """;

        var inputs = YahooDcfInputsSource.Parse(gap)!;

        inputs.TtmRevenue.Should().BeNull("a missing quarter must not be summed over");
        inputs.LatestQuarterEnd.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void Parse_skips_null_and_mislabelled_points_and_returns_null_without_an_annual_period()
    {
        const string junk = """
        { "timeseries": { "result": [
          { "meta": { "type": ["annualTotalRevenue"] }, "annualTotalRevenue": [
            null,
            { "asOfDate": "2025-12-31", "periodType": "3M", "reportedValue": { "raw": 1.0E9 } },
            { "asOfDate": "2024-12-31", "periodType": "12M", "reportedValue": {} } ] }
        ] } }
        """;

        YahooDcfInputsSource.Parse(junk).Should().BeNull();
        YahooDcfInputsSource.Parse("""{ "timeseries": { "result": null } }""").Should().BeNull();
    }
}

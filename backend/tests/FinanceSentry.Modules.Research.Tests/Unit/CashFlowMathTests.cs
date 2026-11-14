namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>#1376: discrete-quarter cash flow, free cash flow, return on equity and their null reasons.</summary>
public sealed class CashFlowMathTests
{
    private static FundamentalFact Duration(
        string concept, string start, string end, decimal value, string form = "10-Q", int fy = 2025)
        => new("ACME", concept, concept, "USD", value, DateOnly.Parse(end), "Q?", fy, form, PeriodStart: DateOnly.Parse(start));

    private static FundamentalFact Point(string concept, string end, decimal value, string fp, int fy = 2025)
        => new("ACME", concept, concept, "USD", value, DateOnly.Parse(end), fp, fy, fp == "FY" ? "10-K" : "10-Q");

    [Fact]
    public void DiscretePeriods_TurnsYearToDateIntoQuartersAndKeepsTheYear()
    {
        var c = CashFlowMath.OperatingCashFlowConcept;
        var periods = CashFlowMath.DiscretePeriods([
            Duration(c, "2025-01-01", "2025-03-31", 100),
            Duration(c, "2025-01-01", "2025-06-30", 250),
            Duration(c, "2025-01-01", "2025-09-30", 450),
            Duration(c, "2025-01-01", "2025-12-31", 700, "10-K"),
        ]);

        periods.Select(p => (p.FiscalPeriod, p.Value)).Should().Equal(
            ("FY", 700m), ("Q4", 250m), ("Q3", 200m), ("Q2", 150m), ("Q1", 100m));
    }

    // AMZN companyconcept NetCashProvidedByUsedInOperatingActivities (EDGAR, live read): the 10-Qs report the
    // discrete three-month figure, the year-to-date one and a trailing-twelve-month one side by side.
    [Fact]
    public void DiscretePeriods_AmazonShapedFilings_LabelDiscreteQuartersByPositionAndDropTrailingTwelveMonths()
    {
        var c = CashFlowMath.OperatingCashFlowConcept;
        var periods = CashFlowMath.DiscretePeriods([
            Duration(c, "2025-04-01", "2025-06-30", 32_515, fy: 2025),
            Duration(c, "2025-01-01", "2025-06-30", 49_530, fy: 2025),
            Duration(c, "2024-07-01", "2025-06-30", 121_137, fy: 2025),
            Duration(c, "2025-07-01", "2025-09-30", 35_525, fy: 2025),
            Duration(c, "2025-01-01", "2025-09-30", 85_055, fy: 2025),
            Duration(c, "2024-10-01", "2025-09-30", 130_691, fy: 2025),
            Duration(c, "2025-01-01", "2025-12-31", 139_514, "10-K", fy: 2025),
            Duration(c, "2026-01-01", "2026-03-31", 26_032, fy: 2026),
            Duration(c, "2025-04-01", "2026-03-31", 148_531, fy: 2026),
        ]);

        periods.Select(p => (p.PeriodEnd.ToString("yyyy-MM-dd"), p.FiscalPeriod, p.Value)).Should().Equal(
            ("2026-03-31", "Q1", 26_032m),
            ("2025-12-31", "FY", 139_514m),
            ("2025-12-31", "Q4", 54_459m),
            ("2025-09-30", "Q3", 35_525m),
            ("2025-06-30", "Q2", 32_515m));
    }

    [Fact]
    public void DiscretePeriods_OmitsAQuarterWhoseEarlierYearToDateIsMissing()
    {
        var c = CashFlowMath.CapexConcept;
        var periods = CashFlowMath.DiscretePeriods([
            Duration(c, "2025-01-01", "2025-09-30", 450),
            Duration(c, "2025-01-01", "2025-12-31", 700, "10-K"),
        ]);

        // No H1 fact: Q3 cannot be derived and is never zero-filled; Q4 (year less nine months) and the year stand.
        periods.Select(p => (p.FiscalPeriod, p.Value)).Should().Equal(("FY", 700m), ("Q4", 250m));
    }

    [Fact]
    public void FreeCashFlow_IsOperatingCashFlowLessCapex_WithYearOverYearTrend()
    {
        var facts = new[]
        {
            Point("OperatingCashFlow", "2025-03-31", 100, "Q1", 2025), Point("Capex", "2025-03-31", 30, "Q1", 2025),
            Point("OperatingCashFlow", "2024-03-31", 80, "Q1", 2024), Point("Capex", "2024-03-31", 40, "Q1", 2024),
            Point("OperatingCashFlow", "2024-12-31", 500, "FY", 2024), Point("Capex", "2024-12-31", 100, "FY", 2024),
        };

        var fcf = DerivedFundamentalsMath.FreeCashFlow(facts, quarters: 5, years: 5);

        var q1 = fcf.Single(p => p.PeriodEnd == new DateOnly(2025, 3, 31));
        q1.FreeCashFlow.Should().Be(70m);
        q1.YoyChange.Should().Be((70m - 40m) / 40m);
        fcf.Single(p => p.Basis == FundamentalsBasis.Annual).FreeCashFlow.Should().Be(400m);
        fcf.Single(p => p.PeriodEnd == new DateOnly(2024, 3, 31)).YoyChange.Should().BeNull();
    }

    [Fact]
    public void FreeCashFlow_IsNullWithReason_WhenCapexIsMissing_NeverZeroFilled()
    {
        var fcf = DerivedFundamentalsMath.FreeCashFlow([Point("OperatingCashFlow", "2025-12-31", 900, "FY")], 5, 5);

        var year = fcf.Should().ContainSingle().Subject;
        year.FreeCashFlow.Should().BeNull();
        year.Capex.Should().BeNull();
        year.OperatingCashFlow.Should().Be(900m);
        year.Reason.Should().Contain("capex not reported");
    }

    [Fact]
    public void FreeCashFlow_CoversTheRequestedNumberOfQuarters()
    {
        var facts = Enumerable.Range(0, 8).SelectMany(i =>
        {
            var end = new DateOnly(2025, 12, 31).AddMonths(-3 * i);
            return new[]
            {
                Point("OperatingCashFlow", end.ToString("yyyy-MM-dd"), 100 + i, "Q4"),
                Point("Capex", end.ToString("yyyy-MM-dd"), 10, "Q4"),
            };
        }).ToList();

        var fcf = DerivedFundamentalsMath.FreeCashFlow(facts, quarters: 5, years: 5);

        fcf.Should().HaveCount(5);
        fcf.Select(p => p.PeriodEnd).Should().BeInDescendingOrder();
    }

    [Fact]
    public void ReturnOnEquity_IsNetIncomeOverYearEndEquity_NullWithReasonOtherwise()
    {
        var facts = new[]
        {
            Point("NetIncome", "2025-12-31", 200, "FY"), Point("StockholdersEquity", "2025-12-31", 1000, "FY"),
            Point("NetIncome", "2024-12-31", 100, "FY", 2024), Point("StockholdersEquity", "2024-12-31", -50, "FY", 2024),
            Point("NetIncome", "2023-12-31", 90, "FY", 2023),
        };

        var roe = DerivedFundamentalsMath.ReturnOnEquity(facts, 5);

        roe.Single(r => r.FiscalYear == 2025).ReturnOnEquity.Should().Be(0.2m);
        roe.Single(r => r.FiscalYear == 2024).Reason.Should().Contain("not positive");
        roe.Single(r => r.FiscalYear == 2023).Reason.Should().Contain("equity not reported");
        roe.Should().OnlyContain(r => r.ReturnOnEquity != 0);
    }

    [Fact]
    public async Task Handler_WithNoCashFlowFacts_ReturnsEmptyFcfWithReason()
    {
        var fundamentals = new FakeFundamentalsService([Point("Revenue", "2025-12-31", 1000, "FY")]);
        var sut = new GetFundamentalsQueryHandler(fundamentals);

        FundamentalsDto dto = await sut.Handle(new GetFundamentalsQuery("ACME", 5), CancellationToken.None);

        dto.FreeCashFlow.Should().BeEmpty();
        dto.FreeCashFlowReason.Should().NotBeNullOrWhiteSpace();
        dto.ReturnOnEquity.Should().BeEmpty();
        dto.ReturnOnEquityReason.Should().NotBeNullOrWhiteSpace();
        dto.Facts.Should().ContainSingle();
    }

    [Fact]
    public async Task Handler_WithCashFlowFacts_ReturnsFcfAndStandaloneCapex()
    {
        var fundamentals = new FakeFundamentalsService([
            Point("OperatingCashFlow", "2025-12-31", 900, "FY"), Point("Capex", "2025-12-31", 300, "FY")]);
        var sut = new GetFundamentalsQueryHandler(fundamentals);

        var dto = await sut.Handle(new GetFundamentalsQuery("ACME", 5), CancellationToken.None);

        dto.FreeCashFlow.Should().ContainSingle().Which.FreeCashFlow.Should().Be(600m);
        dto.FreeCashFlowReason.Should().BeNull();
        dto.Facts.Select(f => f.Concept).Should().BeEquivalentTo("Capex", "OperatingCashFlow");
    }

    [Fact]
    public async Task Handler_ReadsEnoughHistoryForRequestedYearsAndTheirYearEarlierComparison()
    {
        // Five cash-flow facts a year (Q1-Q4 and FY, Q4 and FY sharing a period end), as EDGAR yields them.
        var facts = new List<FundamentalFact>();
        for (var i = 0; i <= 5; i++)
        {
            var year = 2020 + i;
            foreach (var (fp, end) in new[] { ("Q1", $"{year}-03-31"), ("Q2", $"{year}-06-30"), ("Q3", $"{year}-09-30"), ("Q4", $"{year}-12-31"), ("FY", $"{year}-12-31") })
            {
                var annual = fp == "FY";
                facts.Add(Point("OperatingCashFlow", end, annual ? 1000 + (100 * i) : 250 + (25 * i), fp, year));
                facts.Add(Point("Capex", end, annual ? 400 : 100, fp, year));
            }
        }

        var sut = new GetFundamentalsQueryHandler(new TrimmingFundamentalsService(facts));

        var dto = await sut.Handle(new GetFundamentalsQuery("ACME", 3), CancellationToken.None);

        var annualPeriods = dto.FreeCashFlow.Where(p => p.Basis == FundamentalsBasis.Annual).ToList();
        annualPeriods.Select(p => p.PeriodEnd.Year).Should().Equal(2025, 2024, 2023);
        annualPeriods.Select(p => p.YoyChange).Should().Equal(
            (1100m - 1000m) / 1000m, (1000m - 900m) / 900m, (900m - 800m) / 800m);
        dto.FreeCashFlow.Count(p => p.Basis == FundamentalsBasis.Quarterly).Should().Be(5);
    }

    [Fact]
    public async Task Handler_AskingForMoreYearsThanTheFactCapHolds_ExplainsTheMissingYearEarlierComparison()
    {
        var facts = new List<FundamentalFact>
        {
            Point("OperatingCashFlow", "2026-03-31", 300, "Q1", 2026), Point("Capex", "2026-03-31", 100, "Q1", 2026),
        };
        for (var year = 2020; year <= 2025; year++)
        {
            foreach (var (fp, end) in new[] { ("Q1", $"{year}-03-31"), ("Q2", $"{year}-06-30"), ("Q3", $"{year}-09-30"), ("Q4", $"{year}-12-31"), ("FY", $"{year}-12-31") })
            {
                facts.Add(Point("OperatingCashFlow", end, fp == "FY" ? 1000 + (100 * (year - 2020)) : 250, fp, year));
                facts.Add(Point("Capex", end, fp == "FY" ? 400 : 100, fp, year));
            }
        }

        var sut = new GetFundamentalsQueryHandler(new TrimmingFundamentalsService(facts));

        var dto = await sut.Handle(new GetFundamentalsQuery("ACME", 5), CancellationToken.None);

        // 20 facts a concept reach back only to the second quarter of 2022: four fiscal years, not the five asked for.
        var annual = dto.FreeCashFlow.Where(p => p.Basis == FundamentalsBasis.Annual).ToList();
        annual.Select(p => p.PeriodEnd.Year).Should().Equal(2025, 2024, 2023, 2022);
        annual.Take(3).Should().OnlyContain(p => p.YoyChange != null && p.YoyReason == null);
        annual[3].YoyChange.Should().BeNull();
        annual[3].YoyReason.Should().Contain("a year earlier");
    }

    private sealed class TrimmingFundamentalsService(IReadOnlyList<FundamentalFact> facts) : Application.Services.Fundamentals.IFundamentalsService
    {
        private const int ChainFactCap = 20;

        public Task<Application.Services.Fundamentals.FundamentalsResult> GetFundamentalsAsync(
            string ticker, int maxPerConcept, CancellationToken ct = default)
        {
            var trimmed = facts
                .GroupBy(f => f.Concept)
                .SelectMany(g => g.OrderByDescending(f => f.PeriodEnd).Take(Math.Min(maxPerConcept, ChainFactCap)))
                .ToList();
            return Task.FromResult(FakeFundamentalsService.ResultFor(ticker, trimmed));
        }
    }
}

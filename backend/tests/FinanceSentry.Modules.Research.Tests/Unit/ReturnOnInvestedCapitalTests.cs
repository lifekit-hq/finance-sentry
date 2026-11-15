namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

/// <summary>#1376: return on invested capital, its shown inputs and its null reasons.</summary>
public sealed class ReturnOnInvestedCapitalTests
{
    private static FundamentalFact Annual(string concept, decimal value, string end = "2025-12-31", string unit = "USD")
        => new("ACME", concept, concept, unit, value, DateOnly.Parse(end), "FY", DateOnly.Parse(end).Year, "10-K");

    private static List<FundamentalFact> FullYear(string end = "2025-12-31") =>
    [
        Annual("OperatingIncome", 1000, end),
        Annual(DerivedFundamentalsMath.IncomeTaxExpenseConcept, 200, end),
        Annual(DerivedFundamentalsMath.PretaxIncomeConcept, 1000, end),
        Annual("StockholdersEquity", 3000, end),
        Annual(DerivedFundamentalsMath.LongTermDebtConcept, 1500, end),
        Annual(DerivedFundamentalsMath.CurrentDebtConcept, 500, end),
        Annual(DerivedFundamentalsMath.CashConcept, 1000, end),
    ];

    [Fact]
    public void Roic_IsNopatOverDebtPlusEquityLessCash_WithEveryInputShown()
    {
        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(FullYear(), 5).Should().ContainSingle().Subject;

        // tax rate 200/1000 = 20%; NOPAT 1000 x 0.8 = 800; invested capital 2000 + 3000 - 1000 = 4000.
        roic.EffectiveTaxRate.Should().Be(0.2m);
        roic.Nopat.Should().Be(800m);
        roic.Debt.Should().Be(2000m);
        roic.InvestedCapital.Should().Be(4000m);
        roic.ReturnOnInvestedCapital.Should().Be(0.2m);
        roic.Reason.Should().BeNull();
        roic.Unit.Should().Be("USD");
        roic.FiscalYear.Should().Be(2025);
    }

    [Fact]
    public void Roic_TotalBorrowings_ReplaceTheLongTermAndCurrentLines()
    {
        var facts = FullYear().Where(f => f.Concept is not (DerivedFundamentalsMath.LongTermDebtConcept or DerivedFundamentalsMath.CurrentDebtConcept)).ToList();
        facts.Add(Annual(DerivedFundamentalsMath.TotalDebtConcept, 2500));

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single();

        roic.Debt.Should().Be(2500m);
        roic.InvestedCapital.Should().Be(4500m);
    }

    [Fact]
    public void Roic_WithoutACurrentDebtLine_UsesTheLongTermLineAlone()
    {
        var facts = FullYear().Where(f => f.Concept != DerivedFundamentalsMath.CurrentDebtConcept).ToList();

        DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single().Debt.Should().Be(1500m);
    }

    [Theory]
    [InlineData("OperatingIncome", "operating income")]
    [InlineData(DerivedFundamentalsMath.IncomeTaxExpenseConcept, "income tax expense")]
    [InlineData(DerivedFundamentalsMath.PretaxIncomeConcept, "pre-tax income")]
    [InlineData("StockholdersEquity", "stockholders' equity")]
    [InlineData(DerivedFundamentalsMath.CashConcept, "cash and cash equivalents")]
    [InlineData(DerivedFundamentalsMath.LongTermDebtConcept, "debt")]
    public void Roic_AMissingInput_IsNullWithAReasonNamingIt_NeverZeroFilled(string missing, string named)
    {
        var facts = FullYear().Where(f => f.Concept != missing).ToList();

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single();

        roic.ReturnOnInvestedCapital.Should().BeNull();
        roic.Reason.Should().Contain(named).And.Contain("nothing assumed zero");
    }

    [Fact]
    public void Roic_ADebtFreeFilerThatReportsNoDebtLine_IsNotReadAsHavingNoDebt()
    {
        var facts = FullYear().Where(f => f.Concept is not (DerivedFundamentalsMath.LongTermDebtConcept or DerivedFundamentalsMath.CurrentDebtConcept)).ToList();

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single();

        roic.Debt.Should().BeNull();
        roic.ReturnOnInvestedCapital.Should().BeNull();
    }

    [Fact]
    public void Roic_PretaxLoss_HasNoTaxRate()
    {
        var facts = FullYear().Where(f => f.Concept != DerivedFundamentalsMath.PretaxIncomeConcept).ToList();
        facts.Add(Annual(DerivedFundamentalsMath.PretaxIncomeConcept, -50));

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single();

        roic.ReturnOnInvestedCapital.Should().BeNull();
        roic.Reason.Should().Contain("pre-tax income is not positive");
    }

    [Fact]
    public void Roic_ATaxBenefit_IsBoundedAtAZeroRate_AndAnOverOneRateAtOne()
    {
        var benefit = FullYear().Where(f => f.Concept != DerivedFundamentalsMath.IncomeTaxExpenseConcept).ToList();
        benefit.Add(Annual(DerivedFundamentalsMath.IncomeTaxExpenseConcept, -300));
        DerivedFundamentalsMath.ReturnOnInvestedCapital(benefit, 5).Single().EffectiveTaxRate.Should().Be(0m);

        var surtax = FullYear().Where(f => f.Concept != DerivedFundamentalsMath.IncomeTaxExpenseConcept).ToList();
        surtax.Add(Annual(DerivedFundamentalsMath.IncomeTaxExpenseConcept, 4000));
        DerivedFundamentalsMath.ReturnOnInvestedCapital(surtax, 5).Single().EffectiveTaxRate.Should().Be(1m);
    }

    [Fact]
    public void Roic_NonPositiveInvestedCapital_IsNullWithAReason_ButKeepsTheInputs()
    {
        var facts = FullYear().Where(f => f.Concept != DerivedFundamentalsMath.CashConcept).ToList();
        facts.Add(Annual(DerivedFundamentalsMath.CashConcept, 9000));

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single();

        roic.ReturnOnInvestedCapital.Should().BeNull();
        roic.InvestedCapital.Should().Be(-4000m);
        roic.Nopat.Should().Be(800m);
        roic.Reason.Should().Contain("invested capital");
    }

    [Fact]
    public void Roic_InputsInDifferentUnits_AreNotCombined()
    {
        var facts = FullYear().Where(f => f.Concept != DerivedFundamentalsMath.CashConcept).ToList();
        facts.Add(Annual(DerivedFundamentalsMath.CashConcept, 1000, unit: "EUR"));

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Single();

        roic.ReturnOnInvestedCapital.Should().BeNull();
        roic.Unit.Should().BeNull();
        roic.Reason.Should().Contain("different units");
    }

    [Fact]
    public void Roic_ReturnsTheLatestRequestedYearsNewestFirst()
    {
        var facts = FullYear("2025-12-31").Concat(FullYear("2024-12-31")).Concat(FullYear("2023-12-31")).ToList();

        var roic = DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 2);

        roic.Select(r => r.FiscalYear).Should().Equal(2025, 2024);
    }

    [Fact]
    public void Roic_QuarterlyFactsAreIgnored()
    {
        var facts = FullYear();
        facts.Add(new FundamentalFact("ACME", "OperatingIncome", "OperatingIncome", "USD", 1, new DateOnly(2026, 3, 31), "Q1", 2026, "10-Q"));

        DerivedFundamentalsMath.ReturnOnInvestedCapital(facts, 5).Should().ContainSingle().Which.FiscalYear.Should().Be(2025);
    }

    [Fact]
    public async Task Handler_WithNoRoicInputs_ExplainsTheEmptyList()
    {
        var sut = new GetFundamentalsQueryHandler(new FakeFundamentalsService([]));

        var dto = await sut.Handle(new GetFundamentalsQuery("ACME", 5), CancellationToken.None);

        dto.ReturnOnInvestedCapital.Should().BeEmpty();
        dto.ReturnOnInvestedCapitalReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Handler_WithTheInputs_ReturnsRoicAlongsideTheFacts()
    {
        var sut = new GetFundamentalsQueryHandler(new FakeFundamentalsService(FullYear()));

        var dto = await sut.Handle(new GetFundamentalsQuery("ACME", 5), CancellationToken.None);

        dto.ReturnOnInvestedCapital.Should().ContainSingle().Which.ReturnOnInvestedCapital.Should().Be(0.2m);
        dto.ReturnOnInvestedCapitalReason.Should().BeNull();
    }
}

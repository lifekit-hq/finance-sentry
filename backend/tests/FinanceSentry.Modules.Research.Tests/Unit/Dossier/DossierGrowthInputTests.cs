namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

public class DossierGrowthInputTests
{
    private const string Eps = "DilutedEPS";

    [Fact]
    public void PositiveBaseYoy_PositivePrior_IsTheChange()
    {
        var facts = new[] { Fact(Eps, 2026, "Q2", 1.2m), Fact(Eps, 2025, "Q2", 1.0m) };

        GetAssetDossierQueryHandler.PositiveBaseYoy(facts, Eps, ThesisPeriodType.Quarter).Should().Be(0.2m);
    }

    [Theory]
    [InlineData("Q2", ThesisPeriodType.Quarter)]
    [InlineData("FY", ThesisPeriodType.Annual)]
    public void PositiveBaseYoy_LossToProfitTurnaround_IsNotEvaluable(string period, ThesisPeriodType periodType)
    {
        var facts = new[] { Fact(Eps, 2026, period, 0.20m), Fact(Eps, 2025, period, -0.40m) };

        GetAssetDossierQueryHandler.PositiveBaseYoy(facts, Eps, periodType).Should().BeNull();
    }

    [Fact]
    public void PositiveBaseYoy_NarrowingLoss_IsNotEvaluable()
    {
        var facts = new[] { Fact(Eps, 2026, "Q2", -0.10m), Fact(Eps, 2025, "Q2", -0.40m) };

        GetAssetDossierQueryHandler.PositiveBaseYoy(facts, Eps, ThesisPeriodType.Quarter).Should().BeNull();
    }

    [Fact]
    public void PositiveBaseYoy_ZeroPrior_IsNotEvaluable()
    {
        var facts = new[] { Fact("Revenue", 2026, "Q2", 10m), Fact("Revenue", 2025, "Q2", 0m) };

        GetAssetDossierQueryHandler.PositiveBaseYoy(facts, "Revenue", ThesisPeriodType.Quarter).Should().BeNull();
    }

    [Fact]
    public void PositiveBaseYoy_NoPriorYearFact_IsNotEvaluable()
    {
        var facts = new[] { Fact(Eps, 2026, "Q2", 1.2m) };

        GetAssetDossierQueryHandler.PositiveBaseYoy(facts, Eps, ThesisPeriodType.Quarter).Should().BeNull();
    }

    [Fact]
    public void PositiveBaseYoy_NoFactsForTheConcept_IsNotEvaluable()
    {
        var facts = new[] { Fact("Revenue", 2026, "Q2", 10m) };

        GetAssetDossierQueryHandler.PositiveBaseYoy(facts, Eps, ThesisPeriodType.Quarter).Should().BeNull();
    }

    private static FundamentalFact Fact(string concept, int year, string period, decimal value)
        => new("T", concept, concept, "USD", value, new DateOnly(year, 6, 30), period, year, "10-Q");
}

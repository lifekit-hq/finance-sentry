namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Validation;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FluentAssertions;
using Xunit;

public class ThesisTriggerVocabularyTests
{
    [Fact]
    public void Validate_AcceptsTriggers_InTheClosedVocabulary()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("revenue_yoy", "lessThan", 0.05m),
            new("price_drawdown", "greaterThan", 0.30m),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Rejects_MetricOutsideVocabulary()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("management loses credibility", "lessThan", 1m),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().Throw<InvalidThesisTriggerException>()
            .WithMessage("*management loses credibility*");
    }

    [Fact]
    public void Validate_Rejects_UnknownDirection()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("revenue_yoy", "declines", 0.05m),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().Throw<InvalidThesisTriggerScaffoldingException>();
    }

    [Fact]
    public void Validate_Accepts_RelativeReturn_WithBenchmarkAndWindow()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("relative_return", "lessThan", -0.05m, BenchmarkTicker: "SPY", WindowDays: 63),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Rejects_RelativeReturn_MissingBenchmarkTicker()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("relative_return", "lessThan", -0.05m, WindowDays: 63),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().Throw<InvalidThesisTriggerScaffoldingException>();
    }

    [Fact]
    public void Validate_Rejects_RelativeReturn_MissingWindowDays()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("relative_return", "lessThan", -0.05m, BenchmarkTicker: "SPY"),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().Throw<InvalidThesisTriggerScaffoldingException>();
    }

    [Fact]
    public void Validate_Rejects_NonPositiveConsecutivePeriods()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("revenue_yoy", "lessThan", 0.05m, ConsecutivePeriods: 0),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().Throw<InvalidThesisTriggerScaffoldingException>();
    }

    [Fact]
    public void Validate_Rejects_TwoThresholds_OnTheSameMetricAndDirection()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("revenue_yoy", "lessThan", 0.05m),
            new("revenue_yoy", "lessThan", -0.20m),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().Throw<ContradictoryThesisTriggerException>()
            .WithMessage("*revenue_yoy*lessThan*");
    }

    [Fact]
    public void Validate_Accepts_SameMetric_OppositeDirections()
    {
        var triggers = new List<ThesisInvalidationTrigger>
        {
            new("revenue_yoy", "lessThan", 0.05m),
            new("revenue_yoy", "greaterThan", 0.50m),
        };

        var act = () => ThesisTriggerVocabulary.Validate(triggers);

        act.Should().NotThrow();
    }
}

namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FluentAssertions;
using Xunit;

public sealed class TriggerPrefillTests
{
    private static readonly OpportunityOptions Options = new();

    [Fact]
    public void Propose_NeverOffersAPriceTrigger_ASpotOrRelativeBreachCannotBreakAThesis()
    {
        var triggers = TriggerPrefill.Propose(new FundamentalsScoreDetail(0.15m, 0.40m), Options);

        triggers.Should().NotBeEmpty();
        triggers.Should().OnlyContain(t => ThesisMetric.CanBreakThesis(t.Metric));
        TriggerPrefill.Propose(new FundamentalsScoreDetail(null, null), Options).Should().BeEmpty();
    }

    [Fact]
    public void Propose_AddsRevenueYoyTrigger_OnlyWhenCurrentGrowthIsPositive()
    {
        var withGrowth = TriggerPrefill.Propose(new FundamentalsScoreDetail(0.15m, null), Options);
        var withoutGrowth = TriggerPrefill.Propose(new FundamentalsScoreDetail(-0.05m, null), Options);
        var withNoData = TriggerPrefill.Propose(new FundamentalsScoreDetail(null, null), Options);

        withGrowth.Should().Contain(t => t.Metric == ThesisMetric.RevenueYoy);
        withoutGrowth.Should().NotContain(t => t.Metric == ThesisMetric.RevenueYoy);
        withNoData.Should().NotContain(t => t.Metric == ThesisMetric.RevenueYoy);
    }

    [Fact]
    public void Propose_AddsGrossMarginTrigger_OnlyWhenMarginIsEvaluable()
    {
        var withMargin = TriggerPrefill.Propose(new FundamentalsScoreDetail(null, 0.40m), Options);
        var withoutMargin = TriggerPrefill.Propose(new FundamentalsScoreDetail(null, null), Options);

        var trigger = withMargin.Should().ContainSingle(t => t.Metric == ThesisMetric.GrossMargin).Subject;
        trigger.Threshold.Should().Be(0.40m - Options.GrossMarginPrefillBuffer);
        withoutMargin.Should().NotContain(t => t.Metric == ThesisMetric.GrossMargin);
    }

    [Fact]
    public void Propose_ProducesValidThesisMetrics_ForEveryTrigger()
    {
        var triggers = TriggerPrefill.Propose(new FundamentalsScoreDetail(0.10m, 0.40m), Options);

        triggers.Should().AllSatisfy(t => ThesisMetric.Contains(t.Metric).Should().BeTrue());
    }

    [Fact]
    public void Propose_IsDeterministic()
    {
        var detail = new FundamentalsScoreDetail(0.10m, 0.40m);

        var run1 = TriggerPrefill.Propose(detail, Options);
        var run2 = TriggerPrefill.Propose(detail, Options);

        run1.Should().BeEquivalentTo(run2);
    }
}

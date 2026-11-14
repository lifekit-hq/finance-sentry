namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class NarrativeFalsifierEvaluabilityTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<ThesisEvaluabilityReport> ReportFor(InvestmentThesis thesis)
    {
        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([thesis]);
        var result = await new GetThesisEvaluabilityQueryHandler(repo.Object)
            .Handle(new GetThesisEvaluabilityQuery(UserId), CancellationToken.None);
        return result.Single();
    }

    [Fact]
    public async Task Falsifiers_AreReportedAsModelCheckedNotMonitored()
    {
        var report = await ReportFor(new InvestmentThesis
        {
            Id = Guid.NewGuid(), UserId = UserId, Ticker = "CBRS",
            NarrativeFalsifiers = ["Loses the contract", "CFO departs"],
        });

        report.NarrativeFalsifiers.Should().Equal(
            new NarrativeFalsifierView("Loses the contract", "model-checked, not monitored"),
            new NarrativeFalsifierView("CFO departs", "model-checked, not monitored"));
    }

    [Fact]
    public async Task Falsifiers_DoNotChangeTheExistingVerdict()
    {
        var triggers = new[]
        {
            new ThesisInvalidationTrigger("price_drawdown", "greaterThan", 0.30m),
            new ThesisInvalidationTrigger("management loses credibility", string.Empty, 0m),
        };
        var without = await ReportFor(new InvestmentThesis { Id = Guid.NewGuid(), UserId = UserId, Ticker = "A", InvalidationTriggers = [.. triggers] });
        var with = await ReportFor(new InvestmentThesis
        {
            Id = Guid.NewGuid(), UserId = UserId, Ticker = "A", InvalidationTriggers = [.. triggers],
            NarrativeFalsifiers = ["Loses the contract"],
        });

        with.TriggerCount.Should().Be(without.TriggerCount).And.Be(2);
        with.EvaluableCount.Should().Be(without.EvaluableCount).And.Be(1);
        with.FullyCovered.Should().Be(without.FullyCovered).And.BeFalse();
        with.Triggers.Should().BeEquivalentTo(without.Triggers);
    }

    [Fact]
    public async Task ALegacyThesis_ReportsNoFalsifiers()
    {
        var report = await ReportFor(new InvestmentThesis { Id = Guid.NewGuid(), UserId = UserId, Ticker = "A" });

        report.NarrativeFalsifiers.Should().BeEmpty();
    }
}

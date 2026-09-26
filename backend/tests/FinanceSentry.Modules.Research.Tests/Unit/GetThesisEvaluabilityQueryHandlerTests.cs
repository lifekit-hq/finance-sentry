namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetThesisEvaluabilityQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_FlagsLegacyTrigger_ThatPredatesTheVocabularyGate()
    {
        var thesisId = Guid.NewGuid();
        var thesis = new InvestmentThesis
        {
            Id = thesisId,
            UserId = UserId,
            Ticker = "ACME",
            InvalidationTriggers =
            [
                new ThesisInvalidationTrigger("revenue_yoy", "lessThan", 0.05m),
                new ThesisInvalidationTrigger("management loses credibility", string.Empty, 0m),
            ],
        };

        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([thesis]);

        var handler = new GetThesisEvaluabilityQueryHandler(repo.Object);

        var result = await handler.Handle(new GetThesisEvaluabilityQuery(UserId), CancellationToken.None);

        var report = result.Should().ContainSingle().Subject;
        report.ThesisId.Should().Be(thesisId);
        report.TriggerCount.Should().Be(2);
        report.EvaluableCount.Should().Be(1);
        report.FullyCovered.Should().BeFalse();

        report.Triggers.Should().ContainSingle(t => t.Metric == "revenue_yoy" && t.IsEvaluable);
        report.Triggers.Should().ContainSingle(
            t => t.Metric == "management loses credibility" && !t.IsEvaluable && t.Reason == "unsupported_metric");
    }

    [Fact]
    public async Task Handle_ReportsFullyCovered_WhenEveryTriggerIsEvaluable()
    {
        var thesis = new InvestmentThesis
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Ticker = "ACME",
            InvalidationTriggers = [new ThesisInvalidationTrigger("price_drawdown", "greaterThan", 0.30m)],
        };

        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([thesis]);

        var handler = new GetThesisEvaluabilityQueryHandler(repo.Object);

        var result = await handler.Handle(new GetThesisEvaluabilityQuery(UserId), CancellationToken.None);

        result.Should().ContainSingle().Which.FullyCovered.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReportsNotFullyCovered_WhenThesisHasNoTriggers()
    {
        var thesis = new InvestmentThesis { Id = Guid.NewGuid(), UserId = UserId, Ticker = "ACME" };

        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([thesis]);

        var handler = new GetThesisEvaluabilityQueryHandler(repo.Object);

        var result = await handler.Handle(new GetThesisEvaluabilityQuery(UserId), CancellationToken.None);

        var report = result.Should().ContainSingle().Subject;
        report.TriggerCount.Should().Be(0);
        report.FullyCovered.Should().BeFalse("a thesis with no triggers monitors nothing");
    }
}

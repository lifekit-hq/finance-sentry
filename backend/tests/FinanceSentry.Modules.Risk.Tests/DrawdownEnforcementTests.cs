using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

/// <summary>#700: the owner's recorded drawdown tolerance is a reportable, acknowledgeable risk-layer threshold.</summary>
public sealed class DrawdownEnforcementTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    private readonly RiskEvaluationService _service = new();

    private static readonly BookSnapshot Book = new(
        9000m, 0m, [new BookPosition("AAA", RiskSleeve.Brokerage, 10m, 9000m, 1m)], false, [], 9000m);

    private static readonly RiskRuleSet RuleSet = new() { UserId = UserId };

    [Fact]
    public void Evaluate_DeclinePastTolerance_RaisesAReportableBookViolation()
    {
        var report = _service.Evaluate(Book, RuleSet, [], [], Now, new DrawdownCheck(0.20m, 0.25m));

        var v = report.Violations.Should().ContainSingle().Subject;
        v.RuleKey.Should().Be(RiskRuleKeys.MaxDrawdown);
        v.Subject.Should().Be(RiskRuleKeys.BookSubject);
        v.ObservedValue.Should().Be(0.25m);
        v.LimitValue.Should().Be(0.20m);
        v.ExcessPct.Should().Be(0.05m);
        v.Reportable.Should().BeTrue();
        // peak value 12,000 (9,000 / 0.75); 5 points of it is 600.
        v.ExcessUsd.Should().Be(600m);
    }

    [Theory]
    [InlineData(0.20)]
    [InlineData(0.10)]
    public void Evaluate_DeclineAtOrWithinTolerance_RaisesNothing(double observed)
    {
        var report = _service.Evaluate(Book, RuleSet, [], [], Now, new DrawdownCheck(0.20m, (decimal)observed));

        report.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NoDrawdownCheck_RaisesNothing()
    {
        _service.Evaluate(Book, RuleSet, [], [], Now, null).Violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NoRuleSet_RaisesNothingEvenPastTolerance()
    {
        var report = _service.Evaluate(Book, null, [], [], Now, new DrawdownCheck(0.20m, 0.50m));

        report.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_AcknowledgedDrawdown_IsSilencedUntilItWorsensPastTheStep()
    {
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MaxDrawdown,
            Subject = RiskRuleKeys.BookSubject,
            RemediationNote = "holding through; reviewed at next cadence",
            ObservedAtAck = 0.25m,
            WorseningStepPct = 0.05m,
        };

        var steady = _service.Evaluate(Book, RuleSet, [], [ack], Now, new DrawdownCheck(0.20m, 0.27m));
        var worse = _service.Evaluate(Book, RuleSet, [], [ack], Now, new DrawdownCheck(0.20m, 0.31m));

        steady.Violations.Single().Reportable.Should().BeFalse();
        worse.Violations.Single().Status.Should().Be(PolicyViolationStatus.Worsened);
        worse.Violations.Single().Reportable.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_NoToleranceRecorded_ReturnsNullWithoutReadingHistory()
    {
        var policy = new Mock<IDrawdownPolicySource>();
        policy.Setup(p => p.GetMaxDrawdownAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((decimal?)null);
        var snapshots = new Mock<IHoldingSnapshotRepository>(MockBehavior.Strict);

        var result = await Provider(policy, snapshots).GetAsync(UserId, Book, Now, default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Provider_MeasuresFromTheLookbackWindowAndTheLiveBook()
    {
        var policy = new Mock<IDrawdownPolicySource>();
        policy.Setup(p => p.GetMaxDrawdownAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(0.20m);
        var snapshots = new Mock<IHoldingSnapshotRepository>();
        snapshots.Setup(s => s.ListSinceAsync(UserId, Now.AddDays(-365), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HoldingSnapshot { Symbol = "AAA", Sleeve = RiskSleeve.Brokerage, Quantity = 10m, UsdValue = 12_000m, CapturedAt = Now.AddDays(-30) }]);

        var result = await Provider(policy, snapshots).GetAsync(UserId, Book, Now, default);

        result.Should().Be(new DrawdownCheck(0.20m, 0.25m));
    }

    [Fact]
    public async Task Provider_NothingToStepBetween_ReturnsNull()
    {
        var policy = new Mock<IDrawdownPolicySource>();
        policy.Setup(p => p.GetMaxDrawdownAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(0.20m);
        var snapshots = new Mock<IHoldingSnapshotRepository>();
        snapshots.Setup(s => s.ListSinceAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        (await Provider(policy, snapshots).GetAsync(UserId, Book, Now, default)).Should().BeNull();
    }

    private static DrawdownCheckProvider Provider(Mock<IDrawdownPolicySource> policy, Mock<IHoldingSnapshotRepository> snapshots)
        => new(policy.Object, snapshots.Object, Options.Create(new RiskOptions()));
}

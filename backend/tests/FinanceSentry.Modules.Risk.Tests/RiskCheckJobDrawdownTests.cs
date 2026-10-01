using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FinanceSentry.Modules.Risk.Infrastructure.Jobs;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

/// <summary>
/// #700: the scheduled check enforces the IPS drawdown tolerance on its own - an owner with no risk
/// rule set still gets the policy-violation alert, and a book back inside tolerance clears it.
/// </summary>
public sealed class RiskCheckJobDrawdownTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly BookSnapshot Book = new(
        7000m, 0m, [new BookPosition("AAA", RiskSleeve.Brokerage, 10m, 7000m, 1m)], false, [], 7000m);

    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private RiskCheckJob Job(DrawdownCheck? drawdown)
    {
        var bookReader = new Mock<IBookSnapshotReader>();
        bookReader.Setup(r => r.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Book);

        var ruleSets = new Mock<IRiskRuleSetRepository>();
        ruleSets.Setup(r => r.GetCurrentUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((RiskRuleSet?)null);

        var acks = new Mock<IPolicyViolationAckRepository>();
        acks.Setup(r => r.ListActiveUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var allocation = new Mock<IAllocationPolicySource>();
        allocation.Setup(s => s.GetAllocationTargetsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var drawdownProvider = new Mock<IDrawdownCheckProvider>();
        drawdownProvider
            .Setup(p => p.GetAsync(UserId, Book, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(drawdown);

        var brokenTheses = new Mock<IBrokenThesisReader>();
        brokenTheses.Setup(r => r.ListBrokenAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        return new RiskCheckJob(
            Mock.Of<IBankingTotalsReader>(),
            bookReader.Object,
            ruleSets.Object,
            acks.Object,
            Mock.Of<IHoldingSnapshotRepository>(),
            new RiskEvaluationService(),
            allocation.Object,
            drawdownProvider.Object,
            Mock.Of<ITurnoverTracker>(),
            Mock.Of<IAddToBrokenThesisDetector>(),
            brokenTheses.Object,
            _alerts.Object,
            Mock.Of<IRadarSignalWriter>(),
            Options.Create(new RiskOptions()));
    }

    [Fact]
    public async Task NoRuleSet_DeclinePastTolerance_RaisesThePolicyViolationAlert()
    {
        await Job(new DrawdownCheck(0.20m, 0.30m)).CheckForUserAsync(UserId);

        _alerts.Verify(a => a.GeneratePolicyViolationAlertAsync(
            UserId, RiskRuleKeys.MaxDrawdown, RiskRuleKeys.BookSubject, 0.30m, 0.20m, false, It.IsAny<CancellationToken>()),
            Times.Once);
        _alerts.Verify(a => a.ResolvePolicyViolationAlertAsync(
            UserId, RiskRuleKeys.MaxDrawdown, RiskRuleKeys.BookSubject, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NoRuleSet_BookBackInsideTolerance_ClearsTheAlertAndRaisesNothing()
    {
        await Job(new DrawdownCheck(0.20m, 0.10m)).CheckForUserAsync(UserId);

        _alerts.Verify(a => a.ResolvePolicyViolationAlertAsync(
            UserId, RiskRuleKeys.MaxDrawdown, RiskRuleKeys.BookSubject, It.IsAny<CancellationToken>()),
            Times.Once);
        _alerts.Verify(a => a.GeneratePolicyViolationAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NoToleranceRecorded_DoesNothingAboutDrawdown()
    {
        await Job(null).CheckForUserAsync(UserId);

        _alerts.Verify(a => a.GeneratePolicyViolationAlertAsync(
            It.IsAny<Guid>(), RiskRuleKeys.MaxDrawdown, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

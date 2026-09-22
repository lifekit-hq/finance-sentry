namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using FinanceSentry.Modules.Budgets.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="BudgetBreachDetectionJob"/> (C2, ledger-heartbeat design).
/// Verifies threshold gating (90% / 100%, independently evaluated) and per-budget alert dispatch.
/// Dedup — the part of the design that makes a crossing fire once per budget per month, and that
/// governs a mid-month limit edit or a refund — is the alert generator's responsibility and is
/// pinned in AlertGeneratorServiceTests, not here (same split as the other hygiene sentinels).
/// </summary>
public sealed class BudgetBreachDetectionJobTests
{
    private readonly Mock<IBudgetRepository> _budgets = new();
    private readonly Mock<IMerchantSpendingReader> _spending = new();
    private readonly Mock<ICategoryNormalizationService> _normalization = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));

    public BudgetBreachDetectionJobTests()
    {
        // Identity normalization by default — tests use already-normalized category keys.
        _normalization.Setup(n => n.Normalize(It.IsAny<string>())).Returns((string c) => c);
    }

    private BudgetBreachDetectionJob MakeJob() => MakeJob(_clock);

    private BudgetBreachDetectionJob MakeJob(TimeProvider clock) =>
        new(_budgets.Object, _spending.Object, _normalization.Object, _alerts.Object, clock,
            NullLogger<BudgetBreachDetectionJob>.Instance);

    private static FixedClock At(int year, int month, int day) =>
        new(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));

    private void SetMonthSpend(Guid userId, string category, int year, int month, decimal usdAmount)
    {
        var from = new DateOnly(year, month, 1);
        _spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                userId, from, from.AddMonths(1).AddDays(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { [category] = usdAmount });
    }

    private static Budget MakeBudget(Guid userId, string category, decimal limit, string currency = "USD")
    {
        var budget = Budget.Create(userId, category, limit, currency);
        return budget;
    }

    private void SetSpend(Guid userId, string category, decimal usdAmount)
        => _spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { [category] = usdAmount });

    [Fact]
    public async Task ExecuteAsync_SpendAt92Percent_FiresNearLimitOnly()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 92m);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 92m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SpendAt105Percent_FiresBothNearLimitAndExceeded()
    {
        // A jump straight past both bars in one run still fires both — they're independent checks.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 105m);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 105m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            userId, budget.Id, "GROCERIES", 105m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SpendBelow90Percent_FiresNothing()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 50m);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoBudgets_FiresNothing()
    {
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await MakeJob().ExecuteAsync();

        _spending.Verify(s => s.GetSpendingByCategoryUsdAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoSpendInCategory_TreatsAsZero_FiresNothing()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        _spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ConvertsNonUsdLimitToUsd_BeforeComparing()
    {
        // 4000 UAH at the FallbackRates 0.024 rate is 96 USD — 90 USD spend is 93.75% of that,
        // above the 90% bar. A raw (unconverted) comparison would have read as far below it.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 4000m, "UAH");
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 90m);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 90m, 96m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleUsers_BatchesSpendReadPerUser()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var budgetA = MakeBudget(userA, "GROCERIES", 100m);
        var budgetB = MakeBudget(userB, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budgetA, budgetB]);
        SetSpend(userA, "GROCERIES", 95m);
        SetSpend(userB, "GROCERIES", 20m);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userA, budgetA.Id, "GROCERIES", 95m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userB, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesOtherBudgets_WhenOneAlertThrows()
    {
        var userId = Guid.NewGuid();
        var budgetA = MakeBudget(userId, "GROCERIES", 100m);
        var budgetB = MakeBudget(userId, "DINING", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budgetA, budgetB]);
        _spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["GROCERIES"] = 95m, ["DINING"] = 95m });
        _alerts.Setup(a => a.GenerateBudgetNearLimitAlertAsync(
                userId, budgetA.Id, "GROCERIES", 95m, 100m, 2026, 9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db failure"));

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budgetB.Id, "DINING", 95m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Decision: a mid-month limit edit is never snapshotted. Each run reads the budget's CURRENT
    /// MonthlyLimit fresh (there is nothing else it could read — the job has no state between runs),
    /// so a limit lowered mid-month can turn spend that wasn't a crossing before into one now.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_LimitLoweredMidMonth_CrossesExceededThatDidNotCrossUnderOldLimit()
    {
        var userId = Guid.NewGuid();
        SetSpend(userId, "GROCERIES", 150m);

        // Run 1: under the original $200 limit, 150/200 = 75% — no crossing.
        var originalBudget = MakeBudget(userId, "GROCERIES", 200m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([originalBudget]);
        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        // Run 2: the user lowers the limit to $100 mid-month — same spend, now 150% — exceeded fires
        // using the live limit, not anything cached from run 1.
        var editedBudget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([editedBudget]);
        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            userId, editedBudget.Id, "GROCERIES", 150m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Decision: a refund is never a reason to retract an alert already raised — BudgetBreach has no
    /// Resolve method, like the other hygiene sentinels. The job simply reports what it observes each
    /// run: spend that drops back under 90% after a refund produces no alert on that run (there is
    /// nothing here to un-fire); if spend climbs back over the line later in the month, the job fires
    /// again on that run too — it is the alert generator's active-alert dedup (pinned separately) that
    /// keeps that second call from producing a second alert for the month.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RefundDropsSpendBelowThreshold_ThenClimbsBackUp_JobReportsBothRuns()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);

        // Run 1: spend crosses 90%.
        SetSpend(userId, "GROCERIES", 92m);
        await MakeJob().ExecuteAsync();
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 92m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);

        // Run 2: a refund drops spend back under 90% — the job reports nothing this run.
        SetSpend(userId, "GROCERIES", 70m);
        await MakeJob().ExecuteAsync();
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        // Run 3: spend climbs back over 90% — the job calls the generator again (its own dedup, not
        // the job's, is what keeps this from becoming a second alert row for the month).
        SetSpend(userId, "GROCERIES", 95m);
        await MakeJob().ExecuteAsync();
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 95m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BudgetsReadFails_LogsAndReturns()
    {
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await MakeJob().ExecuteAsync();

        _spending.Verify(s => s.GetSpendingByCategoryUsdAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SpendReadFailsForOneUser_OtherUserStillEvaluated()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var budgetA = MakeBudget(userA, "GROCERIES", 100m);
        var budgetB = MakeBudget(userB, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budgetA, budgetB]);
        _spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                userA, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("read failed"));
        SetSpend(userB, "GROCERIES", 95m);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userB, budgetB.Id, "GROCERIES", 95m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CrossingOnLastDayOfMonth_AlertsOnNextRun()
    {
        // Spend posted on Sep 30 lands after the Sep 30 00:00 run; the Oct 1 run must still see it.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 104m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 0m);

        await MakeJob(At(2026, 10, 1)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            userId, budget.Id, "GROCERIES", 104m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 104m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_LatePostingIntoPreviousMonth_InsideGraceWindow_Alerts()
    {
        // A September transaction that only syncs on Oct 6 pushes September over 90%.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 93m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 10m);

        await MakeJob(At(2026, 10, 7)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 93m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BothMonthsEvaluated_EachCrossingReportedOncePerMonth()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 105m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 95m);

        await MakeJob(At(2026, 10, 2)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 105m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            userId, budget.Id, "GROCERIES", 105m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 95m, 100m, 2026, 10, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PastGraceWindow_PreviousMonthBreachDoesNotResurface()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 150m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 10m);

        await MakeJob(At(2026, 10, 8)).ExecuteAsync();

        _spending.Verify(s => s.GetSpendingByCategoryUsdAsync(
            userId, new DateOnly(2026, 9, 1), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// End to end through the real alert generator: a September breach alert the user dismissed
    /// stays dismissed when the Oct 1 run re-evaluates September inside the grace window.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DismissedPriorMonthAlert_StaysSilentInGraceWindowRun()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 104m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 0m);

        var ledger = new List<Alert>();
        var repo = new Mock<IAlertRepository>();
        repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid u, string type, Guid? referenceId, CancellationToken _) =>
                ledger.Any(a => a.UserId == u && a.Type == type && a.ReferenceId == referenceId));
        repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Callback<Alert, CancellationToken>((a, _) => ledger.Add(a))
            .Returns(Task.CompletedTask);
        var job = new BudgetBreachDetectionJob(
            _budgets.Object, _spending.Object, _normalization.Object, new AlertGeneratorService(repo.Object),
            At(2026, 9, 30), NullLogger<BudgetBreachDetectionJob>.Instance);

        await job.ExecuteAsync();
        Assert.Equal(2, ledger.Count);
        foreach (var alert in ledger)
        {
            alert.IsDismissed = true;
            alert.CreatedAt = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        }

        job = new BudgetBreachDetectionJob(
            _budgets.Object, _spending.Object, _normalization.Object, new AlertGeneratorService(repo.Object),
            At(2026, 10, 1), NullLogger<BudgetBreachDetectionJob>.Instance);
        await job.ExecuteAsync();

        Assert.Equal(2, ledger.Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

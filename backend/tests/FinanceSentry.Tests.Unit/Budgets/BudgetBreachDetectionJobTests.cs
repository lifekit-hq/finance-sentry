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
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 16, 23, 55, 0, TimeSpan.Zero));

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
        new(new DateTimeOffset(year, month, day, 23, 55, 0, TimeSpan.Zero));

    private static FixedClock At(int year, int month, int day, int hour, int minute) =>
        new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

    private (List<Alert> Ledger, AlertGeneratorService Generator) RealGenerator()
    {
        var ledger = new List<Alert>();
        var repo = new Mock<IAlertRepository>();
        repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid u, string type, Guid? referenceId, CancellationToken _) =>
                ledger.Any(a => a.UserId == u && a.Type == type && a.ReferenceId == referenceId));
        repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Callback<Alert, CancellationToken>((a, _) => ledger.Add(a))
            .Returns(Task.CompletedTask);
        return (ledger, new AlertGeneratorService(repo.Object));
    }

    private BudgetBreachDetectionJob MakeJob(IAlertGeneratorService generator, TimeProvider clock) =>
        new(_budgets.Object, _spending.Object, _normalization.Object, generator, clock,
            NullLogger<BudgetBreachDetectionJob>.Instance);

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
    public async Task ExecuteAsync_LateRunOnLastDayOfMonth_SeesThatDaysSpend()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 104m);

        await MakeJob(new FixedClock(new DateTimeOffset(2026, 9, 30, 23, 55, 0, TimeSpan.Zero))).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            userId, budget.Id, "GROCERIES", 104m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.GenerateBudgetNearLimitAlertAsync(
            userId, budget.Id, "GROCERIES", 104m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// End to end through the real alert generator: breach alerts the user dismissed stay dismissed
    /// when a later run in the same month still sees spend over both thresholds.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DismissedAlert_StaysSilentOnLaterRunSameMonth()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 104m);

        var (ledger, generator) = RealGenerator();
        var job = MakeJob(generator, At(2026, 9, 5));

        await job.ExecuteAsync();
        Assert.Equal(2, ledger.Count);
        foreach (var alert in ledger)
        {
            alert.IsDismissed = true;
            alert.CreatedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        }

        job = MakeJob(generator, At(2026, 9, 30));
        await job.ExecuteAsync();

        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public async Task ExecuteAsync_LastDaySlotStartingAfterMidnight_EvaluatesThatMonth()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 104m);

        await MakeJob(At(2026, 10, 1, 0, 3)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetExceededAlertAsync(
            userId, budget.Id, "GROCERIES", 104m, 100m, 2026, 9, It.IsAny<CancellationToken>()), Times.Once);
        _spending.Verify(s => s.GetSpendingByCategoryUsdAsync(
            userId, new DateOnly(2026, 10, 1), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1, 3, 0)]
    [InlineData(1, 12, 0)]
    [InlineData(3, 9, 0)]
    public async Task ExecuteAsync_StartingPastMaxDelayAfterSlot_IsSkipped(int day, int hour, int minute)
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 150m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 150m);

        await MakeJob(At(2026, 10, day, hour, minute)).ExecuteAsync();

        _budgets.Verify(b => b.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        _alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_OnTimeThenLateThenSkippedRuns_RaiseEachCrossingOncePerMonth()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 104m);
        SetMonthSpend(userId, "GROCERIES", 2026, 10, 104m);
        var (ledger, generator) = RealGenerator();

        await MakeJob(generator, At(2026, 9, 30)).ExecuteAsync();
        await MakeJob(generator, At(2026, 10, 1, 0, 3)).ExecuteAsync();
        await MakeJob(generator, At(2026, 10, 1, 4, 0)).ExecuteAsync();

        Assert.Equal(2, ledger.Count);
        Assert.All(ledger, a => Assert.Contains("September 2026", a.Title));
    }

    [Fact]
    public async Task ExecuteAsync_PaceRatioAt115OnDay14_FiresPaceAlert()
    {
        // Day 14 of a 30-day September: elapsed fraction 14/30. 46 USD spent against a 100 USD
        // limit gives a pace ratio of 46 / (100 * 14/30) = ~0.986 — just under. Bump to 47 USD to
        // clear 1.15: 47 / (100 * 14/30) ≈ 1.007, still under. Use 58 USD: 58 / 46.67 ≈ 1.243 — over.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 58m);

        await MakeJob(At(2026, 9, 14)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetPaceAlertAsync(
            userId, budget.Id, "GROCERIES", 58m, 100m, It.Is<decimal>(p => p > 124m && p < 125m),
            2026, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PaceRatioJustBelow115OnDay14_FiresNoPaceAlert()
    {
        // 53 USD spent, elapsed fraction 14/30 -> pace ratio 53 / 46.67 ≈ 1.136 — under 1.15.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 53m);

        await MakeJob(At(2026, 9, 14)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetPaceAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PaceRatioOver115OnDay6_FiresNoPaceAlert()
    {
        // Before the day-7 window opens, even a wildly off-pace spend does not fire — the small
        // elapsed-fraction denominator this early would invent a crisis out of one large charge.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 90m);

        await MakeJob(At(2026, 9, 6)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetPaceAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PaceRatioOver115OnDay22_FiresNoPaceAlert()
    {
        // Past the day-21 window close, the exceeded/near-limit thresholds are the better signal —
        // a pace alert here would be a second notification about the same thing.
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        SetSpend(userId, "GROCERIES", 85m);

        await MakeJob(At(2026, 9, 22)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateBudgetPaceAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// End to end through the real alert generator: a second evaluation later in the same month's
    /// pace window does not re-fire the pace alert for the same budget/month.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SecondEvaluationSameMonth_DoesNotRefirePaceAlert()
    {
        var userId = Guid.NewGuid();
        var budget = MakeBudget(userId, "GROCERIES", 100m);
        _budgets.Setup(b => b.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        // 78 USD spent stays over the 1.15 pace ratio on both day 14 (ratio ~1.67) and day 20
        // (ratio ~1.17), while staying under the 90% near-limit threshold throughout, so the pace
        // dedup is exercised in isolation, with no near-limit/exceeded alert also landing in the
        // ledger.
        SetMonthSpend(userId, "GROCERIES", 2026, 9, 78m);
        var (ledger, generator) = RealGenerator();

        await MakeJob(generator, At(2026, 9, 14)).ExecuteAsync();
        Assert.Single(ledger);

        await MakeJob(generator, At(2026, 9, 20)).ExecuteAsync();

        Assert.Single(ledger);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

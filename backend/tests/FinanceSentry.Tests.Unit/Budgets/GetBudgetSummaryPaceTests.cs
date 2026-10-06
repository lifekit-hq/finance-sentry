namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Queries;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using Moq;
using Xunit;

/// <summary>
/// Pace fields on <see cref="GetBudgetSummaryQueryHandler"/>: the summary exposes the same
/// <c>BudgetPace</c> numbers the breach job alerts on, so the page and the agent tool agree.
/// </summary>
public sealed class GetBudgetSummaryPaceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Task<FinanceSentry.Modules.Budgets.API.Responses.BudgetSummaryResponse> Run(
        DateTimeOffset now, decimal spent, int? year = null, int? month = null)
    {
        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Budget.Create(UserId, "FOOD", 100m, "USD")]);
        var normalization = new Mock<ICategoryNormalizationService>();
        normalization.Setup(n => n.Normalize(It.IsAny<string>())).Returns((string c) => c);
        normalization.Setup(n => n.GetLabel(It.IsAny<string>())).Returns((string c) => c);
        var spending = new Mock<IMerchantSpendingReader>();
        spending.Setup(s => s.GetSpendingByCategoryUsdAsync(
                UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["FOOD"] = spent });

        var handler = new GetBudgetSummaryQueryHandler(
            budgets.Object, normalization.Object, spending.Object, new FixedClock(now));
        return handler.Handle(new GetBudgetSummaryQuery(UserId, year, month), CancellationToken.None);
    }

    [Fact]
    public async Task CurrentMonth_EightyPercentOnDay14_IsOffPaceWithProjection()
    {
        // Day 14 of 30 (September): 80 spent → 80 / (100 × 14/30) ≈ 1.714, projected ≈ 171.4.
        var item = (await Run(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), 80m)).Items.Single();

        Assert.True(item.IsOffPace);
        Assert.Equal(1.714m, Math.Round(item.PaceRatio, 3));
        Assert.Equal(171.4m, Math.Round(item.ProjectedMonthEndSpend, 1));
    }

    [Fact]
    public async Task CurrentMonth_EightyPercentOnDay28_IsNotOffPace()
    {
        var item = (await Run(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), 80m)).Items.Single();

        Assert.False(item.IsOffPace);
        Assert.Equal(0.857m, Math.Round(item.PaceRatio, 3));
    }

    [Fact]
    public async Task CurrentMonth_AfterPaceWindow_IsNotOffPaceButKeepsPaceRatio()
    {
        // Day 22 of 30: 85 spent → ratio 85 / (100 × 22/30) ≈ 1.159, past the job's day-21 alert window.
        var item = (await Run(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), 85m)).Items.Single();

        Assert.False(item.IsOffPace);
        Assert.Equal(1.159m, Math.Round(item.PaceRatio, 3));
    }

    [Fact]
    public async Task CurrentMonth_Day3_IsNeverOffPace()
    {
        var item = (await Run(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero), 90m)).Items.Single();

        Assert.False(item.IsOffPace);
    }

    [Fact]
    public async Task CurrentMonth_AlreadyOverBudget_IsOverNotOffPace()
    {
        var item = (await Run(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), 120m)).Items.Single();

        Assert.True(item.IsOverBudget);
        Assert.False(item.IsOffPace);
    }

    [Fact]
    public async Task PastMonth_PaceRatioEqualsSpentOverLimit_AndIsNeverOffPace()
    {
        var item = (await Run(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), 80m, 2026, 8)).Items.Single();

        Assert.Equal(0.8m, item.PaceRatio);
        Assert.Equal(80m, item.ProjectedMonthEndSpend);
        Assert.False(item.IsOffPace);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

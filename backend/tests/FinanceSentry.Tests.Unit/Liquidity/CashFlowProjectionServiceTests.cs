namespace FinanceSentry.Tests.Unit.Liquidity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Liquidity.Application.Services;
using Xunit;

public class CashFlowProjectionServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 22);

    [Fact]
    public void Project_BalanceSufficient_ReturnsNull()
    {
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Netflix", "monthly", 15m, "EUR", Today.AddDays(5)),
        };

        var result = CashFlowProjectionService.Project(200m, "EUR", subscriptions, Today);

        Assert.Null(result);
    }

    [Fact]
    public void Project_OutflowExceedsBalance_ReturnsShortfall()
    {
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Netflix", "monthly", 250m, "EUR", Today.AddDays(10)),
        };

        var result = CashFlowProjectionService.Project(200m, "EUR", subscriptions, Today);

        Assert.NotNull(result);
        Assert.Equal(Today.AddDays(10), result.Date);
        Assert.Equal(50m, result.Amount);
    }

    [Fact]
    public void Project_MultipleOutflowsSameDay_AllSubtracted()
    {
        var dueDate = Today.AddDays(7);
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Netflix", "monthly", 15m, "EUR", dueDate),
            new("Spotify", "monthly", 10m, "EUR", dueDate),
        };

        // Balance 20 — after 25 outflow on day 7 → −5 shortfall
        var result = CashFlowProjectionService.Project(20m, "EUR", subscriptions, Today);

        Assert.NotNull(result);
        Assert.Equal(dueDate, result.Date);
        Assert.Equal(5m, result.Amount);
    }

    [Fact]
    public void Project_FirstShortfallReported_NotSubsequentDays()
    {
        var firstDue = Today.AddDays(3);
        var secondDue = Today.AddDays(10);
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Netflix", "monthly", 200m, "EUR", firstDue),
            new("Spotify", "monthly", 200m, "EUR", secondDue),
        };

        var result = CashFlowProjectionService.Project(100m, "EUR", subscriptions, Today);

        Assert.NotNull(result);
        // First shortfall is on day 3, not day 10
        Assert.Equal(firstDue, result.Date);
        Assert.Equal(100m, result.Amount);
    }

    [Fact]
    public void Project_NoSubscriptions_ReturnsNull()
    {
        var result = CashFlowProjectionService.Project(0m, "EUR", [], Today);

        Assert.Null(result);
    }

    [Fact]
    public void Project_ZeroBalanceNoOutflows_ReturnsNull()
    {
        var result = CashFlowProjectionService.Project(0m, "EUR", [], Today);

        Assert.Null(result);
    }

    [Fact]
    public void Project_OutflowAfter30Days_Ignored()
    {
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Netflix", "monthly", 5000m, "EUR", Today.AddDays(31)),
        };

        var result = CashFlowProjectionService.Project(1m, "EUR", subscriptions, Today);

        Assert.Null(result);
    }

    [Fact]
    public void Project_CrossCurrencySubscription_Excluded()
    {
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Netflix", "monthly", 250m, "USD", Today.AddDays(5)),
        };

        // Account is EUR, subscription is USD — must be excluded
        var result = CashFlowProjectionService.Project(100m, "EUR", subscriptions, Today);

        Assert.Null(result);
    }

    [Fact]
    public void Project_AnnualSubscriptionWithinWindow_Included()
    {
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Adobe", "annual", 600m, "EUR", Today.AddDays(15)),
        };

        var result = CashFlowProjectionService.Project(100m, "EUR", subscriptions, Today);

        Assert.NotNull(result);
        Assert.Equal(Today.AddDays(15), result.Date);
        Assert.Equal(500m, result.Amount);
    }

    [Fact]
    public void Project_ShortfallAmountIsPositive()
    {
        var subscriptions = new List<ActiveSubscriptionSummary>
        {
            new("Gym", "monthly", 100m, "EUR", Today.AddDays(1)),
        };

        var result = CashFlowProjectionService.Project(0m, "EUR", subscriptions, Today);

        Assert.NotNull(result);
        Assert.True(result.Amount > 0, "Shortfall amount must be positive.");
        Assert.Equal(100m, result.Amount);
    }
}

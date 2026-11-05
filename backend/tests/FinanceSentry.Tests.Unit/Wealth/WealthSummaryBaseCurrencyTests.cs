namespace FinanceSentry.Tests.Unit.Wealth;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.API.Controllers;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>#851: /wealth/summary amounts are expressed in, and labelled with, the profile base currency.</summary>
public sealed class WealthSummaryBaseCurrencyTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();

    public void Dispose() => CurrencyConverter.UpdateRates(CurrencyConverter.FallbackRates);

    private static AccountBalanceDto Account() => new(
        Guid.NewGuid(), "AIB", "current", "1111", "truelayer", "banking",
        "USD", 1080m, 1080m, "synced", null);

    private static WealthSummaryResponse UsdSummary()
    {
        var account = Account();
        var institution = new InstitutionDto(
            Guid.NewGuid(), "truelayer", "AIB", "banking", 1080m, "synced", null, null, [account]);
        return new WealthSummaryResponse(
            1080m, "USD", [new CategorySummaryDto("banking", 1080m, 1, [institution])],
            new AppliedFiltersDto(null, null));
    }

    private static async Task<WealthSummaryResponse> Get(string? profileCurrency)
    {
        var summary = new Mock<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>();
        summary.Setup(h => h.Handle(It.IsAny<GetWealthSummaryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsdSummary());
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            summary.Object,
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>(),
            Mock.Of<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>(),
            Mock.Of<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>(),
            Mock.Of<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>(),
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetSummary(null, null, CancellationToken.None);
        return (WealthSummaryResponse)ok.Value!;
    }

    [Fact]
    public async Task EuroBase_ConvertsEveryAmount_AndLabelsThemEur()
    {
        var result = await Get("EUR");

        var institution = result.Categories[0].Institutions[0];
        result.BaseCurrency.Should().Be("EUR");
        result.TotalNetWorth.Should().Be(1000m);
        result.Categories[0].TotalInBaseCurrency.Should().Be(1000m);
        institution.TotalInBaseCurrency.Should().Be(1000m);
        institution.Accounts[0].BalanceInBaseCurrency.Should().Be(1000m);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    public async Task UsdOrUnsetBase_LeavesAmountsUntouched(string? profile)
    {
        var result = await Get(profile);

        result.BaseCurrency.Should().Be("USD");
        result.TotalNetWorth.Should().Be(1080m);
        result.Categories[0].Institutions[0].Accounts[0].BalanceInBaseCurrency.Should().Be(1080m);
    }

    private static async Task<FireProjectionResponse> GetFire(string? profileCurrency)
    {
        var fire = new Mock<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>();
        fire.Setup(h => h.Handle(It.IsAny<GetFireProjectionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireProjectionResponse(
                FireProjectionStatus.Projected, Target: 1080m, CurrentNetWorth: 540m, MonthlySavings: 108m,
                AnnualSpend: 43.2m, SafeWithdrawalRate: 0.04m, RealAnnualReturn: 0.05m,
                ProjectedDate: new DateOnly(2040, 1, 1), MonthsToFire: 120m, HasStaleSleeves: false));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            Mock.Of<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            fire.Object,
            Mock.Of<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>(),
            Mock.Of<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>(),
            Mock.Of<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>(),
            Mock.Of<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>(),
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetFireProjection(CancellationToken.None);
        return (FireProjectionResponse)ok.Value!;
    }

    [Fact]
    public async Task Fire_EuroBase_ConvertsEveryAmount_AndLabelsThemEur()
    {
        var result = await GetFire("EUR");

        result.BaseCurrency.Should().Be("EUR");
        result.Target.Should().Be(1000m);
        result.CurrentNetWorth.Should().Be(500m);
        result.MonthlySavings.Should().Be(100m);
        result.AnnualSpend.Should().Be(40m);
        result.SafeWithdrawalRate.Should().Be(0.04m);
        result.MonthsToFire.Should().Be(120m);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    public async Task Fire_UsdOrUnsetBase_LeavesAmountsUntouched(string? profile)
    {
        var result = await GetFire(profile);

        result.BaseCurrency.Should().Be("USD");
        result.Target.Should().Be(1080m);
        result.AnnualSpend.Should().Be(43.2m);
    }

    private static async Task<NetWorthProjectionResponse> GetProjection(string? profileCurrency)
    {
        var projection = new Mock<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>();
        projection.Setup(h => h.Handle(It.IsAny<GetNetWorthProjectionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthProjectionResponse(
                NetWorthProjectionStatus.Projected, HorizonMonths: 12, CurrentNetWorth: 540m, MonthlySavings: 108m,
                CompleteMonths: 3, ProjectedNetWorth: new ProjectionRange(1080m, 1296m, 1512m),
                RealAnnualReturnRange: new ProjectionRange(0.02m, 0.05m, 0.08m), HasStaleSleeves: false));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            Mock.Of<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>(),
            projection.Object,
            Mock.Of<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>(),
            Mock.Of<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>(),
            Mock.Of<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>(),
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetNetWorthProjection(CancellationToken.None);
        return (NetWorthProjectionResponse)ok.Value!;
    }

    [Fact]
    public async Task Projection_EuroBase_ConvertsEveryAmountAndTheWholeRange_NotTheRates()
    {
        var result = await GetProjection("EUR");

        result.BaseCurrency.Should().Be("EUR");
        result.CurrentNetWorth.Should().Be(500m);
        result.MonthlySavings.Should().Be(100m);
        result.ProjectedNetWorth.Should().Be(new ProjectionRange(1000m, 1200m, 1400m));
        result.RealAnnualReturnRange.Should().Be(new ProjectionRange(0.02m, 0.05m, 0.08m));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    public async Task Projection_UsdOrUnsetBase_LeavesAmountsUntouched(string? profile)
    {
        var result = await GetProjection(profile);

        result.BaseCurrency.Should().Be("USD");
        result.ProjectedNetWorth.Should().Be(new ProjectionRange(1080m, 1296m, 1512m));
    }

    private static async Task<MonthlyIncomeLensResponse> GetIncomeLens(string? profileCurrency)
    {
        var lens = new Mock<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>();
        lens.Setup(h => h.Handle(It.IsAny<GetMonthlyIncomeLensQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyIncomeLensResponse(
                MonthlyIncomeLensStatus.Shown, NetWorth: 54_000m, SafeWithdrawalRate: 0.04m, MonthlyIncome: 108m,
                HasStaleSleeves: false));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            Mock.Of<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>(),
            Mock.Of<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>(),
            lens.Object,
            Mock.Of<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>(),
            Mock.Of<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>(),
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetMonthlyIncomeLens(CancellationToken.None);
        return (MonthlyIncomeLensResponse)ok.Value!;
    }

    [Fact]
    public async Task IncomeLens_EuroBase_ConvertsBothAmounts_NotTheRate()
    {
        var result = await GetIncomeLens("EUR");

        result.BaseCurrency.Should().Be("EUR");
        result.NetWorth.Should().Be(50_000m);
        result.MonthlyIncome.Should().Be(100m);
        result.SafeWithdrawalRate.Should().Be(0.04m);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    public async Task IncomeLens_UsdOrUnsetBase_LeavesAmountsUntouched(string? profile)
    {
        var result = await GetIncomeLens(profile);

        result.BaseCurrency.Should().Be("USD");
        result.MonthlyIncome.Should().Be(108m);
    }

    private static async Task<SleeveGrowthResponse> GetGrowth(string? profileCurrency)
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 10, 14);
        var headline = HeadlineFactory.Growth(
            108m, MeasuredRate.Of(0.1m), HeadlineScope.Sleeve(WealthSourceIds.Brokerage), from, to, HeadlineSignals.None);
        var growth = new Mock<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>();
        growth.Setup(h => h.Handle(It.IsAny<GetSleeveGrowthQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SleeveGrowthResponse(
                from, from, to, 1080m, 1296m, 108m, 0m, 108m, 10.8m, 0m, 0m, 0.1m, [], headline));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            Mock.Of<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>(),
            growth.Object,
            Mock.Of<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>(),
            Mock.Of<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>(),
            Mock.Of<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>(),
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetBrokerageGrowth(CancellationToken.None);
        return (SleeveGrowthResponse)ok.Value!;
    }

    [Fact]
    public async Task BrokerageGrowth_EuroBase_ConvertsEveryAmount_AndNamesTodaysRate()
    {
        var result = await GetGrowth("EUR");

        result.Currency.Should().Be("EUR");
        result.StartValue.Should().Be(1000m);
        result.EndValue.Should().Be(1200m);
        result.Contributed.Should().Be(100m);
        result.Growth.Should().Be(100m);
        result.Income.Should().Be(10m);
        result.MoneyWeightedReturn.Should().Be(0.1m);
        result.Headline.Amount.Should().Be(100m);
        result.Headline.Currency.Should().Be("EUR");
        result.Headline.Reasons.Should().Contain(r => r.Code == HeadlineReasonCode.FxTodayRate);
    }

    [Fact]
    public async Task BrokerageGrowth_UsdBase_LeavesAmountsUntouched()
    {
        var result = await GetGrowth("USD");

        result.Currency.Should().Be("USD");
        result.Growth.Should().Be(108m);
        result.Headline.Amount.Should().Be(108m);
    }
    private static async Task<CashflowProfileResponse> GetCashflowProfile(string? profileCurrency)
    {
        var profile = new Mock<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>();
        profile.Setup(h => h.Handle(It.IsAny<GetCashflowProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashflowProfileResponse(
                CashflowProfileStatus.Shown, CompleteMonths: 6, FromMonth: "2026-03", ToMonth: "2026-08",
                IncomeMonthlyMedian: 4_320m, BurnMonthlyMedian: 3_240m, SavingsRate: 0.25m, RunwayMonths: 6.0m,
                RunwayReason: null, CommittedShare: 0.2m, HasStaleSleeves: false));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            Mock.Of<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>(),
            Mock.Of<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>(),
            profile.Object,
            Mock.Of<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>(),
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetCashflowProfile(CancellationToken.None);
        return (CashflowProfileResponse)ok.Value!;
    }

    [Fact]
    public async Task CashflowProfile_EuroBase_ConvertsTheTwoMonthlyAmounts_NotTheRateRunwayOrShare()
    {
        var result = await GetCashflowProfile("EUR");

        result.BaseCurrency.Should().Be("EUR");
        result.IncomeMonthlyMedian.Should().Be(4_000m);
        result.BurnMonthlyMedian.Should().Be(3_000m);
        result.SavingsRate.Should().Be(0.25m);
        result.RunwayMonths.Should().Be(6.0m);
        result.CommittedShare.Should().Be(0.2m);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    public async Task CashflowProfile_UsdOrUnsetBase_LeavesAmountsUntouched(string? profile)
    {
        var result = await GetCashflowProfile(profile);

        result.BaseCurrency.Should().Be("USD");
        result.IncomeMonthlyMedian.Should().Be(4_320m);
        result.BurnMonthlyMedian.Should().Be(3_240m);
    }

    private static async Task<GoalsProgressResponse> GetGoals(string? profileCurrency)
    {
        var goals = new Mock<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>>();
        goals.Setup(h => h.Handle(It.IsAny<GetGoalsProgressQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalsProgressResponse(
                GoalsProgressStatus.Shown,
                CurrentNetWorth: 540m,
                MonthlySavings: 54m,
                CompleteMonths: 6,
                [
                    new GoalProgress(
                        "House deposit", 1080m, new DateOnly(2030, 1, 1), "high", MonthsLeft: 40,
                        RequiredMonthlySaving: new ProjectionRange(10.8m, 16.2m, 21.6m),
                        ProjectedAtTargetDate: new ProjectionRange(1080m, 1188m, 1296m),
                        AlreadyReached: false, GoalTrack.OnTrack),
                    new GoalProgress(
                        "Someday", null, null, "low", MonthsLeft: null, RequiredMonthlySaving: null,
                        ProjectedAtTargetDate: null, AlreadyReached: false, GoalTrack.NoTargetAmount),
                ],
                new ContributionPlanComparison(
                    PlannedAmount: 27m, "weekly", PlannedMonthly: 108m, ActualMonthlySavings: 54m, Difference: -54m),
                new ProjectionRange(0.02m, 0.05m, 0.08m),
                HasStaleSleeves: false));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new WealthController(
            Mock.Of<IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>>(),
            Mock.Of<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>>(),
            Mock.Of<IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>>(),
            Mock.Of<IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>>(),
            Mock.Of<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>>(),
            goals.Object,
            reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetGoalsProgress(CancellationToken.None);
        return (GoalsProgressResponse)ok.Value!;
    }

    [Fact]
    public async Task Goals_EuroBase_ConvertsEveryAmount_AndLabelsThemEur()
    {
        var result = await GetGoals("EUR");

        result.BaseCurrency.Should().Be("EUR");
        result.CurrentNetWorth.Should().Be(500m);
        result.MonthlySavings.Should().Be(50m);
        var house = result.Goals[0];
        house.TargetAmount.Should().Be(1000m);
        house.RequiredMonthlySaving.Should().Be(new ProjectionRange(10m, 15m, 20m));
        house.ProjectedAtTargetDate.Should().Be(new ProjectionRange(1000m, 1100m, 1200m));
        house.MonthsLeft.Should().Be(40, "a month count is not money");
        result.ContributionPlan!.PlannedAmount.Should().Be(25m);
        result.ContributionPlan.PlannedMonthly.Should().Be(100m);
        result.ContributionPlan.ActualMonthlySavings.Should().Be(50m);
        result.ContributionPlan.Difference.Should().Be(-50m);
        result.RealAnnualReturnRange.Should().Be(new ProjectionRange(0.02m, 0.05m, 0.08m), "returns are not money");
    }

    [Fact]
    public async Task Goals_EuroBase_LeavesAbsentFiguresAbsent()
    {
        var result = await GetGoals("EUR");

        var someday = result.Goals[1];
        someday.TargetAmount.Should().BeNull();
        someday.RequiredMonthlySaving.Should().BeNull();
        someday.ProjectedAtTargetDate.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("USD")]
    public async Task Goals_UsdOrUnsetBase_LeavesAmountsUntouched(string? profile)
    {
        var result = await GetGoals(profile);

        result.BaseCurrency.Should().Be("USD");
        result.Goals[0].TargetAmount.Should().Be(1080m);
        result.ContributionPlan!.Difference.Should().Be(-54m);
    }
}

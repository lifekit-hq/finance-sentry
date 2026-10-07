namespace FinanceSentry.Tests.Unit.BankSync.Application;

using System.Security.Claims;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>#851: the dashboard totals follow the profile base currency, USD when unset.</summary>
public sealed class DashboardBaseCurrencyTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();

    public void Dispose() => CurrencyConverter.UpdateRates(CurrencyConverter.FallbackRates);

    private static DashboardData UsdData() => new(
        new Dictionary<string, decimal> { ["EUR"] = 1000m },
        TotalNetWorthUsd: 1080m,
        AccountCount: 1,
        AccountsByType: new Dictionary<string, int> { ["checking"] = 1 },
        MonthlyFlow: [new MonthlyFlow("2026-10", "EUR", 3000m, 3100m, -100m, 3240m, 3348m, -108m, 0m, 0m, 108m, 0m)],
        TopCategories: [new CategoryStat("GROCERIES", 108m, 100m)],
        LastSyncTimestamp: null);

    private static async Task<DashboardData> Get(string? profileCurrency)
    {
        var dashboard = new Mock<IDashboardQueryService>();
        dashboard.Setup(d => d.GetDashboardDataAsync(UserId, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsdData());
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new DashboardController(
            dashboard.Object, Mock.Of<ITransactionRepository>(), Mock.Of<IBankAccountRepository>(),
            Mock.Of<ITransferDetectionService>(), Mock.Of<IFlowBreakdownService>(), reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

        var ok = (OkObjectResult)await controller.GetAggregated();
        return (DashboardData)ok.Value!;
    }

    [Fact]
    public async Task EuroBase_ReturnsTotalsInEuros_AndNamesTheCurrency()
    {
        var data = await Get("EUR");

        data.BaseCurrency.Should().Be("EUR");
        data.MonthlyFlow[0].OutflowUsd.Should().Be(3100m);
        data.MonthlyFlow[0].InflowUsd.Should().Be(3000m);
        data.TotalNetWorthUsd.Should().Be(1000m);
        data.TopCategories[0].TotalSpend.Should().Be(100m);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    public async Task UsdOrUnsetBase_LeavesTotalsUntouched(string? profile)
    {
        var data = await Get(profile);

        data.BaseCurrency.Should().Be("USD");
        data.MonthlyFlow[0].OutflowUsd.Should().Be(3348m);
        data.TotalNetWorthUsd.Should().Be(1080m);
    }
}

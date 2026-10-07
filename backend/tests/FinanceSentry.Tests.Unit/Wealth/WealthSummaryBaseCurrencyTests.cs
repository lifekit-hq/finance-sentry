namespace FinanceSentry.Tests.Unit.Wealth;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
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
}

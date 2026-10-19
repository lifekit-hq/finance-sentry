namespace FinanceSentry.Tests.Unit.Wealth;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.API.Controllers;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

public class NetWorthBridgeControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Opening = new(2026, 9, 1);
    private static readonly DateOnly Closing = new(2026, 9, 11);

    private static readonly NetWorthBridgeResponse UsdBridge = new(
        Opening, Closing, StartValue: 1_000m, EndValue: 1_200m, Savings: 150m, InvestmentReturn: 0m, Market: 0m, Income: 0m,
        Fees: 0m, Taxes: 0m, FxTranslation: 0m, Revaluation: 0m, NewlyLinked: 0m, Unlinked: 0m, Residual: 50m,
        ResidualAboveTolerance: true, HeadlineQuality.Degraded, [new HeadlineReason(HeadlineReasonCode.ResidualAboveTolerance)]);

    private static async Task<NetWorthBridgeResponse> GetBridgeAsync(string baseCurrency, StubPublishedRates rates)
    {
        var handler = new Mock<IQueryHandler<GetNetWorthBridgeQuery, NetWorthBridgeResponse>>();
        handler.Setup(h => h.Handle(It.IsAny<GetNetWorthBridgeQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(UsdBridge);
        var currencies = new Mock<IUserBaseCurrencyReader>();
        currencies.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(baseCurrency);

        var controller = new NetWorthBridgeController(handler.Object, currencies.Object, rates)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")),
                },
            },
        };

        var result = await controller.GetBridge();
        return result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<NetWorthBridgeResponse>().Subject;
    }

    [Fact]
    public async Task GetBridge_PassesUsdThrough()
    {
        var result = await GetBridgeAsync("USD", new StubPublishedRates());

        result.Should().Be(UsdBridge);
    }

    [Fact]
    public async Task GetBridge_ConvertsEachEndAtItsOwnRate_AndTheLinesStillSumToTheConvertedChange()
    {
        // 1.25 USD per EUR at the opening, 1.00 at the closing: the start reads 800 EUR, the end 1,200 EUR, and the
        // start's re-statement (1,000 × (1.0 − 0.8)) joins the FX line.
        var rates = new StubPublishedRates(new() { ["EUR"] = new() { [Opening] = 1.25m, [Closing] = 1.00m } });

        var result = await GetBridgeAsync("EUR", rates);

        result.Currency.Should().Be("EUR");
        result.StartValue.Should().Be(800m);
        result.EndValue.Should().Be(1_200m);
        result.Savings.Should().Be(150m);
        result.FxTranslation.Should().Be(200m);
        result.Residual.Should().Be(50m);
        (result.Savings + result.InvestmentReturn + result.FxTranslation + result.Revaluation + result.NewlyLinked
            - result.Unlinked + result.Residual).Should().Be(result.EndValue - result.StartValue);
        result.Quality.Should().Be(HeadlineQuality.Degraded);
        result.Reasons.Should().ContainSingle(r => r.Code == HeadlineReasonCode.ResidualAboveTolerance);
    }
}

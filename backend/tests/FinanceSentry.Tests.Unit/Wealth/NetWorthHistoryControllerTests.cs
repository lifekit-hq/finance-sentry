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

public class NetWorthHistoryControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<NetWorthHistoryResponse> GetHistoryAsync(string baseCurrency, params NetWorthSnapshotDto[] snapshots)
    {
        var handler = new Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>>();
        handler.Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse(snapshots, snapshots.Length > 0));
        var currencies = new Mock<IUserBaseCurrencyReader>();
        currencies.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(baseCurrency);

        var controller = new NetWorthHistoryController(handler.Object, currencies.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")),
                },
            },
        };

        var result = await controller.GetHistory();
        return result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<NetWorthHistoryResponse>().Subject;
    }

    [Fact]
    public async Task GetHistory_ConvertsTheCashAndInvestedSplitWithTheTotals_WhenTheBaseCurrencyIsNotUsd()
    {
        var day = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 2), 1_000m, 500m, 250m, 1_750m, "USD", null, false,
            CashTotal: 1_150m, BrokerageInvested: 400m, CryptoInvested: 200m);

        var usd = await GetHistoryAsync("USD", day);
        var eur = await GetHistoryAsync("EUR", day);

        var rate = CurrencyConverter.FromUsd(1m, "EUR");
        rate.Should().NotBe(1m, "the test needs a base currency whose rate differs from USD");
        var row = eur.Snapshots.Single();
        row.Currency.Should().Be("EUR");
        row.TotalNetWorth.Should().Be(CurrencyConverter.FromUsd(1_750m, "EUR"));
        row.CashTotal.Should().Be(CurrencyConverter.FromUsd(1_150m, "EUR"));
        row.BrokerageInvested.Should().Be(CurrencyConverter.FromUsd(400m, "EUR"));
        row.CryptoInvested.Should().Be(CurrencyConverter.FromUsd(200m, "EUR"));
        (row.CashTotal + row.BrokerageInvested + row.CryptoInvested).Should().BeApproximately(row.TotalNetWorth, 0.0001m);
        usd.Snapshots.Single().CashTotal.Should().Be(1_150m);
    }

    [Fact]
    public async Task GetHistory_KeepsADayWithNoSplitNull_WhenTheBaseCurrencyIsNotUsd()
    {
        var day = new NetWorthSnapshotDto(new DateOnly(2026, 5, 1), 900m, 0m, 0m, 900m, "USD", null, false);

        var row = (await GetHistoryAsync("EUR", day)).Snapshots.Single();

        row.CashTotal.Should().BeNull();
        row.BrokerageInvested.Should().BeNull();
        row.CryptoInvested.Should().BeNull();
    }
}

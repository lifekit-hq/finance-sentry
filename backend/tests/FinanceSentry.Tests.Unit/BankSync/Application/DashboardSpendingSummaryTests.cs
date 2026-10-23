namespace FinanceSentry.Tests.Unit.BankSync.Application;

using System.Security.Claims;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>
/// <c>GET /dashboard/spending-summary</c>: the ledger's outflow and top category follow the
/// account and day range asked for, in the profile's base currency.
/// </summary>
public sealed class DashboardSpendingSummaryTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();

    public void Dispose() => CurrencyConverter.UpdateRates(CurrencyConverter.FallbackRates);

    private static (DashboardController Controller, Mock<IFlowBreakdownService> Breakdown) Build(string? profileCurrency)
    {
        var breakdown = new Mock<IFlowBreakdownService>();
        breakdown
            .Setup(b => b.GetSpendingSummaryAsync(UserId, It.IsAny<Guid?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpendingSummary(1080m, "GROCERIES"));
        var reader = new Mock<IUserBaseCurrencyReader>();
        reader.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(profileCurrency!);

        var controller = new DashboardController(
            Mock.Of<IDashboardQueryService>(), Mock.Of<ITransactionRepository>(), Mock.Of<IBankAccountRepository>(),
            Mock.Of<ITransferDetectionService>(), breakdown.Object, reader.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };
        return (controller, breakdown);
    }

    [Fact]
    public async Task ForwardsTheAccountAndRange_AndAnswersInTheBaseCurrency()
    {
        var (controller, breakdown) = Build("EUR");
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 23);

        var ok = (OkObjectResult)await controller.GetSpendingSummary(AccountId, from, to);

        var body = (SpendingSummaryResponse)ok.Value!;
        body.Currency.Should().Be("EUR");
        body.Outflow.Should().Be(1000m);
        body.TopCategory.Should().Be("GROCERIES");
        breakdown.Verify(b => b.GetSpendingSummaryAsync(UserId, AccountId, from, to, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnsetBase_AnswersInUsd()
    {
        var (controller, _) = Build(null);

        var ok = (OkObjectResult)await controller.GetSpendingSummary();

        var body = (SpendingSummaryResponse)ok.Value!;
        body.Currency.Should().Be("USD");
        body.Outflow.Should().Be(1080m);
    }

    [Fact]
    public async Task ARangeEndingBeforeItStarts_IsRejected()
    {
        var (controller, breakdown) = Build("USD");

        var result = await controller.GetSpendingSummary(null, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 1));

        result.Should().BeOfType<BadRequestObjectResult>();
        breakdown.Verify(b => b.GetSpendingSummaryAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

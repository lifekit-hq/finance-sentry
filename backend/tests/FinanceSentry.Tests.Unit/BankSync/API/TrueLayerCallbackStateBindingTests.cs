namespace FinanceSentry.Tests.Unit.BankSync.API;

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The TrueLayer OAuth callback is JWT-exempt and identifies the pending connection by its
/// <c>state</c>. It must finalize only in the browser that started the connect flow: the
/// connect call sets a state cookie, and a callback that does not present it is not attached
/// to the initiating user's connection.
/// </summary>
public class TrueLayerCallbackStateBindingTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private const string OwnerState = "0f8fad5bd9cb469fa16570867728950e";
    private const string OtherState = "7c9e6679742540de944be07fc1f90ae7";
    private const string StateCookie = "fs_truelayer_state";
    private const string Code = "auth-code";

    private readonly Mock<ICommandHandler<BeginTrueLayerConnectCommand, BeginTrueLayerConnectResult>> _begin = new();
    private readonly Mock<ICommandHandler<FinalizeTrueLayerConnectCommand, FinalizeTrueLayerConnectResult>> _finalize = new();

    public TrueLayerCallbackStateBindingTests()
    {
        _begin
            .Setup(h => h.Handle(It.IsAny<BeginTrueLayerConnectCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeginTrueLayerConnectResult("https://auth.truelayer.com/?state=" + OwnerState, OwnerState));
        _finalize
            .Setup(h => h.Handle(It.IsAny<FinalizeTrueLayerConnectCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinalizeTrueLayerConnectResult(OwnerId, 1, [Guid.NewGuid()]));
    }

    [Fact]
    public async Task BeginConnect_SetsHttpOnlyLaxStateCookie()
    {
        var controller = CreateController(authenticated: true);

        await controller.BeginTrueLayerConnect(new BeginTrueLayerConnectRequest("ob-revolut", "Revolut"), default);

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain($"{StateCookie}={OwnerState}");
        setCookie.Should().ContainEquivalentOf("httponly");
        setCookie.Should().ContainEquivalentOf("samesite=lax");
    }

    [Fact]
    public async Task Callback_WithoutStateCookie_IsRejectedAndNotFinalized()
    {
        var controller = CreateController(authenticated: false);

        var result = await controller.TrueLayerCallback(Code, OwnerState, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().EndWith("connectError=TRUELAYER_STATE_MISMATCH");
        _finalize.Verify(
            h => h.Handle(It.IsAny<FinalizeTrueLayerConnectCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_WithStateCookieOfAnotherFlow_IsRejectedAndNotFinalized()
    {
        var controller = CreateController(authenticated: false, stateCookie: OtherState);

        var result = await controller.TrueLayerCallback(Code, OwnerState, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().EndWith("connectError=TRUELAYER_STATE_MISMATCH");
        _finalize.Verify(
            h => h.Handle(It.IsAny<FinalizeTrueLayerConnectCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_InBrowserThatStartedFlow_FinalizesAndClearsCookie()
    {
        var controller = CreateController(authenticated: false, stateCookie: OwnerState);

        var result = await controller.TrueLayerCallback(Code, OwnerState, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().EndWith("connected=truelayer");
        _finalize.Verify(
            h => h.Handle(
                It.Is<FinalizeTrueLayerConnectCommand>(c => c.Reference == OwnerState && c.Code == Code),
                It.IsAny<CancellationToken>()),
            Times.Once);
        controller.Response.Headers.SetCookie.ToString().Should().Contain($"{StateCookie}=;");
    }

    private BankSyncController CreateController(bool authenticated, string? stateCookie = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrueLayer:FrontendRedirectBase"] = "http://localhost:4200"
            })
            .Build();

        var controller = new BankSyncController(
            new Mock<ICommandHandler<ConnectMonobankAccountCommand, ConnectMonobankResult>>().Object,
            new Mock<IQueryHandler<GetAccountsQuery, GetAccountsResult>>().Object,
            new Mock<IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult>>().Object,
            new Mock<IQueryHandler<ListTrueLayerProvidersQuery, IReadOnlyList<TrueLayerProviderDto>>>().Object,
            _begin.Object,
            _finalize.Object,
            new Mock<ICommandHandler<DisconnectInstitutionCommand, DisconnectInstitutionResult>>().Object,
            configuration,
            NullLogger<BankSyncController>.Instance,
            new Mock<IBankAccountRepository>().Object,
            new Mock<ITransactionRepository>().Object,
            new Mock<IBackgroundJobClient>().Object,
            new Mock<IRecurringJobManager>().Object,
            new Mock<ISyncJobRepository>().Object,
            new Mock<ITransactionSyncCoordinator>().Object,
            new Mock<IAlertGeneratorService>().Object,
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development));

        var httpContext = new DefaultHttpContext();
        if (authenticated)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString())], "test"));
        }
        if (stateCookie != null)
            httpContext.Request.Headers.Cookie = $"{StateCookie}={stateCookie}";

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}

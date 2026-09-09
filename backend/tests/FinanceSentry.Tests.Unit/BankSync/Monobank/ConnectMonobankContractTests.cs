namespace FinanceSentry.Tests.Unit.BankSync.Monobank;

using System.Security.Claims;
using System.Text.Json;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.API.Middleware;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Contract tests for POST /api/v1/accounts/monobank/connect (T011):
/// 201 created, 400 invalid token, 409 duplicate, 429 rate limit.
/// The 201 path exercises the real controller action; the error paths run the
/// real command handler through ErrorHandlingMiddleware, which owns the
/// exception → HTTP status mapping for the endpoint. No database, no live HTTP.
/// </summary>
public class ConnectMonobankContractTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private const string Token = "uTestToken123";

    private static readonly MonobankAccountInfo BlackAccount = new(
        Id: "kKGVoZuHWzqVoZuH",
        Name: "black UAH",
        Type: "checking",
        MaskedPan: "537541******1234",
        CurrencyCode: 980,
        Balance: 1234567,
        CreditLimit: 0);

    private readonly Mock<IMonobankAdapter> _adapter = new();
    private readonly Mock<ICredentialEncryptionService> _encryption = new();
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IMonobankCredentialRepository> _credentials = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobs = new();

    public ConnectMonobankContractTests()
    {
        _encryption
            .Setup(e => e.Encrypt(Token))
            .Returns(new EncryptionResult([1, 2, 3], [4, 5, 6], [7, 8, 9], 1));
        _accounts
            .Setup(a => a.AddAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount a, CancellationToken _) => a);
        _credentials
            .Setup(c => c.AddAsync(It.IsAny<MonobankCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonobankCredential c, CancellationToken _) => c);
        _credentials
            .Setup(c => c.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonobankCredential?)null);
    }

    private ConnectMonobankAccountCommandHandler CreateHandler() => new(
        _adapter.Object, _encryption.Object, _accounts.Object,
        _credentials.Object, _backgroundJobs.Object);

    // ── 201 Created ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectMonobank_ValidToken_Returns201WithConnectedAccounts()
    {
        _adapter
            .Setup(a => a.ConnectAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BlackAccount]);

        var controller = CreateController();
        var result = await controller.ConnectMonobank(new ConnectMonobankRequest(Token), default);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(201);

        var body = objectResult.Value.Should().BeOfType<ConnectMonobankResult>().Subject;
        body.Accounts.Should().ContainSingle();
        body.Accounts[0].BankName.Should().Be("Monobank");
        body.Accounts[0].Provider.Should().Be("monobank");
        body.Accounts[0].Currency.Should().Be("UAH");
        body.Accounts[0].AccountNumberLast4.Should().Be("1234");
        body.Accounts[0].CurrentBalance.Should().Be(12345.67m); // 1234567 kopecks ÷ 100
    }

    // ── 400 invalid token ────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectMonobank_InvalidToken_Returns400TokenInvalid()
    {
        _adapter
            .Setup(a => a.ConnectAsync(Token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MonobankException(
                "MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400));

        var (status, errorCode) = await RunThroughErrorMiddlewareAsync(
            () => CreateHandler().Handle(new ConnectMonobankAccountCommand(UserId, Token), default));

        status.Should().Be(400);
        errorCode.Should().Be("MONOBANK_TOKEN_INVALID");
    }

    // ── 409 duplicate connect ────────────────────────────────────────────────

    [Fact]
    public async Task ConnectMonobank_DuplicateToken_Returns409Duplicate()
    {
        _adapter
            .Setup(a => a.ConnectAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BlackAccount]);
        _credentials
            .Setup(c => c.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCredential(UserId, [1], [2], [3], 1));

        var (status, errorCode) = await RunThroughErrorMiddlewareAsync(
            () => CreateHandler().Handle(new ConnectMonobankAccountCommand(UserId, Token), default));

        status.Should().Be(409);
        errorCode.Should().Be("MONOBANK_TOKEN_DUPLICATE");

        _credentials.Verify(
            c => c.AddAsync(It.IsAny<MonobankCredential>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 429 rate limit ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectMonobank_RateLimited_Returns429()
    {
        _adapter
            .Setup(a => a.ConnectAsync(Token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MonobankException(
                "MONOBANK_RATE_LIMITED",
                "Monobank API rate limit exceeded. Please try again in 60 seconds.", 429));

        var (status, errorCode) = await RunThroughErrorMiddlewareAsync(
            () => CreateHandler().Handle(new ConnectMonobankAccountCommand(UserId, Token), default));

        status.Should().Be(429);
        errorCode.Should().Be("MONOBANK_RATE_LIMITED");
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="action"/> behind the real ErrorHandlingMiddleware and
    /// returns the HTTP status code and errorCode the API contract exposes.
    /// </summary>
    private static async Task<(int Status, string? ErrorCode)> RunThroughErrorMiddlewareAsync(
        Func<Task> action)
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => action(), NullLogger<ErrorHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, doc.RootElement.GetProperty("errorCode").GetString());
    }

    private BankSyncController CreateController()
    {
        var controller = new BankSyncController(
            CreateHandler(),
            new Mock<IQueryHandler<GetAccountsQuery, GetAccountsResult>>().Object,
            new Mock<IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult>>().Object,
            new Mock<IQueryHandler<ListTrueLayerProvidersQuery, IReadOnlyList<TrueLayerProviderDto>>>().Object,
            new Mock<ICommandHandler<BeginTrueLayerConnectCommand, BeginTrueLayerConnectResult>>().Object,
            new Mock<ICommandHandler<FinalizeTrueLayerConnectCommand, FinalizeTrueLayerConnectResult>>().Object,
            new Mock<ICommandHandler<DisconnectInstitutionCommand, DisconnectInstitutionResult>>().Object,
            new Mock<IConfiguration>().Object,
            NullLogger<BankSyncController>.Instance,
            _accounts.Object,
            new Mock<ITransactionRepository>().Object,
            _backgroundJobs.Object,
            new Mock<ISyncJobRepository>().Object,
            new Mock<ITransactionSyncCoordinator>().Object,
            new Mock<IAlertGeneratorService>().Object);

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }
}

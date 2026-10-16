namespace FinanceSentry.Tests.Integration.BankSync;

using System.Security.Claims;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Drives the transaction list endpoints with <c>from</c>/<c>to</c> query strings through the real
/// controller, handler and repository against real Postgres. The controller turns the dates into
/// <see cref="DateTime"/> bounds that are written to <c>timestamptz</c> parameters, and Npgsql
/// rejects any bound whose <see cref="DateTimeKind"/> is not UTC — a failure the in-memory provider
/// and the repository-level tests (which hand over UTC bounds themselves) cannot show.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TransactionDateRangeEndpointTests : IAsyncLifetime
{
    private readonly Guid _userId = Guid.NewGuid();
    private TestDatabase? _database;
    private BankAccount _account = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();

        _account = new BankAccount(_userId, Guid.NewGuid().ToString("N")[..12], "Test Bank", "checking", "1234",
            "Test Owner", "EUR", _userId, "test-provider");
        setup.BankAccounts.Add(_account);
        setup.Transactions.AddRange(
            NewTransaction("Before range", Utc(2026, 2, 28, 23, 59), "hash-before"),
            NewTransaction("First day", Utc(2026, 3, 1, 0, 0), "hash-first"),
            NewTransaction("Last day", Utc(2026, 3, 31, 23, 59), "hash-last"),
            NewTransaction("After range", Utc(2026, 4, 1, 0, 0), "hash-after"));
        await setup.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BankSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseNpgsql(_database!.ConnectionString)
            .Options, new FixedCurrentUser(_userId));

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private Transaction NewTransaction(string description, DateTime date, string uniqueHash) =>
        new(_account.Id, _userId, 10m, date, description, uniqueHash);

    private BankSyncController CreateController(BankSyncDbContext context)
    {
        var transactions = new TransactionRepository(context);
        var handler = new GetAllTransactionsQueryHandler(
            transactions, new BankAccountRepository(context), new Mock<ICounterpartyClassificationService>().Object);

        var controller = new BankSyncController(
            new Mock<ICommandHandler<ConnectMonobankAccountCommand, ConnectMonobankResult>>().Object,
            new Mock<IQueryHandler<GetAccountsQuery, GetAccountsResult>>().Object,
            handler,
            new Mock<IQueryHandler<ListTrueLayerProvidersQuery, IReadOnlyList<TrueLayerProviderDto>>>().Object,
            new Mock<ICommandHandler<BeginTrueLayerConnectCommand, BeginTrueLayerConnectResult>>().Object,
            new Mock<ICommandHandler<FinalizeTrueLayerConnectCommand, FinalizeTrueLayerConnectResult>>().Object,
            new Mock<ICommandHandler<DisconnectInstitutionCommand, DisconnectInstitutionResult>>().Object,
            new Mock<IConfiguration>().Object,
            NullLogger<BankSyncController>.Instance,
            new BankAccountRepository(context),
            transactions,
            new Mock<IBackgroundJobClient>().Object,
            new Mock<IRecurringJobManager>().Object,
            new Mock<ISyncJobRepository>().Object,
            new Mock<ITransactionSyncCoordinator>().Object,
            new Mock<IAlertGeneratorService>().Object,
            new Mock<IAccountLifecycle>().Object,
            new Mock<IHostEnvironment>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "test")),
            },
        };
        return controller;
    }

    [DockerRequiredFact]
    public async Task GetAllTransactions_WithoutDateRange_ReturnsEveryTransaction()
    {
        await using var context = CreateContext();

        var result = await CreateController(context).GetAllTransactions();

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<PagedResponse<GlobalTransactionDto>>().Subject;
        page.TotalCount.Should().Be(4);
    }

    [DockerRequiredFact]
    public async Task GetAllTransactions_WithFromAndTo_ReturnsTheInclusiveRange()
    {
        await using var context = CreateContext();

        var result = await CreateController(context).GetAllTransactions(from: "2026-03-01", to: "2026-03-31");

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<PagedResponse<GlobalTransactionDto>>().Subject;
        page.Items.Select(t => t.Description).Should().BeEquivalentTo(["First day", "Last day"]);
    }

    [DockerRequiredFact]
    public async Task GetAllTransactions_WithFromAndToAndAccountId_ReturnsTheInclusiveRange()
    {
        await using var context = CreateContext();

        var result = await CreateController(context).GetAllTransactions(
            from: "2026-03-01", to: "2026-03-31", accountId: [_account.Id]);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<PagedResponse<GlobalTransactionDto>>().Subject;
        page.Items.Select(t => t.Description).Should().BeEquivalentTo(["First day", "Last day"]);
    }

    [DockerRequiredFact]
    public async Task GetTransactions_ForOneAccount_WithoutDateRange_ReturnsEveryTransaction()
    {
        await using var context = CreateContext();

        var result = await CreateController(context).GetTransactions(_account.Id);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<TransactionPageResponse>()
            .Which.TotalCount.Should().Be(4);
    }

    [DockerRequiredFact]
    public async Task GetTransactions_ForOneAccount_WithFromAndTo_ReturnsTheInclusiveRange()
    {
        await using var context = CreateContext();

        var result = await CreateController(context).GetTransactions(_account.Id, from: "2026-03-01", to: "2026-03-31");

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<TransactionPageResponse>().Subject;
        page.Items.Select(t => t.Description).Should().BeEquivalentTo(["First day", "Last day"]);
    }
}

using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BankSync.API;

public class DeleteAccountControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IRecurringJobManager> _recurringJobs = new();
    private readonly Mock<IAccountLifecycle> _lifecycle = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();

    private BankSyncController Controller()
    {
        var controller = new BankSyncController(
            new Mock<ICommandHandler<ConnectMonobankAccountCommand, ConnectMonobankResult>>().Object,
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
            _jobs.Object,
            _recurringJobs.Object,
            new Mock<ISyncJobRepository>().Object,
            new Mock<ITransactionSyncCoordinator>().Object,
            new Mock<IAlertGeneratorService>().Object,
            _lifecycle.Object,
            new Mock<IHostEnvironment>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "test")),
            },
        };
        return controller;
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_recurring_sync_job()
    {
        var account = new BankAccount { UserId = _userId };
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await Controller().DeleteAccount(account.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(account.Id)), Times.Once);
    }

    [Fact]
    public async Task Deleting_someone_elses_account_leaves_the_job_alone()
    {
        var account = new BankAccount { UserId = Guid.NewGuid() };
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await Controller().DeleteAccount(account.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _recurringJobs.Verify(m => m.RemoveIfExists(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reopening_a_retired_account_restores_it_and_syncs_it_now()
    {
        var account = new BankAccount { UserId = _userId };
        account.Retire(DateTimeOffset.UtcNow, AccountClosedReasons.AbsentFromList);
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await Controller().ReopenAccount(account.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _lifecycle.Verify(l => l.ReopenAsync(account, It.IsAny<CancellationToken>()), Times.Once);
        _jobs.Verify(j => j.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()), Times.Once);
    }

    [Fact]
    public async Task Reopening_an_account_that_is_not_retired_is_a_conflict()
    {
        var account = new BankAccount { UserId = _userId };
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await Controller().ReopenAccount(account.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        _lifecycle.Verify(l => l.ReopenAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reopening_someone_elses_account_is_not_found()
    {
        var account = new BankAccount { UserId = Guid.NewGuid() };
        account.Retire(DateTimeOffset.UtcNow, AccountClosedReasons.AbsentFromList);
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await Controller().ReopenAccount(account.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _lifecycle.Verify(l => l.ReopenAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Triggering_a_sync_on_a_retired_account_is_a_conflict()
    {
        var account = new BankAccount { UserId = _userId };
        account.Retire(DateTimeOffset.UtcNow, AccountClosedReasons.AbsentFromList);
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await Controller().TriggerSync(account.Id, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("ACCOUNT_CLOSED", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
    }
}

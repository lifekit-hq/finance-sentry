using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BankSync.Application.Commands;

public class DisconnectInstitutionCommandTests
{
    [Fact]
    public async Task Disconnecting_an_institution_removes_the_recurring_sync_job_of_each_child_account()
    {
        var userId = Guid.NewGuid();
        var institutionId = Guid.NewGuid();
        var child = new BankAccount { UserId = userId, MonobankCredentialId = institutionId };
        var other = new BankAccount { UserId = userId, MonobankCredentialId = Guid.NewGuid() };

        var accounts = new Mock<IBankAccountRepository>();
        accounts.Setup(a => a.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync([child, other]);
        var recurringJobs = new Mock<IRecurringJobManager>();

        var handler = new DisconnectInstitutionCommandHandler(
            accounts.Object,
            new Mock<IMonobankCredentialRepository>().Object,
            new Mock<ITrueLayerConnectionRepository>().Object,
            new Mock<IAlertGeneratorService>().Object,
            recurringJobs.Object);

        var result = await handler.Handle(new DisconnectInstitutionCommand(userId, "monobank", institutionId), CancellationToken.None);

        Assert.Equal(1, result.RemovedAccounts);
        recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(child.Id)), Times.Once);
        recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(other.Id)), Times.Never);
    }
}

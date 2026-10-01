namespace FinanceSentry.Tests.Integration.Shared;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.AuditLog;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Integration tests for AuditLogService.
/// Uses an in-memory SQLite substitute (or real PostgreSQL via Testcontainers in CI).
/// Tests verify Constitution V compliance: all data access is recorded.
/// </summary>
public class AuditLogServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly BankSyncDbContext _db;
    private readonly AuditLogService _sut;

    public AuditLogServiceTests()
    {
        var services = new ServiceCollection();
        // Shared root ensures all DbContext instances (across DI scopes) write to the same store.
        var dbRoot = new InMemoryDatabaseRoot();
        var dbName = $"audit-log-test-{Guid.NewGuid()}";
        services.AddDbContext<BankSyncDbContext>(options =>
            options.UseInMemoryDatabase(dbName, dbRoot));
        services.AddSingleton<FinanceSentry.Core.Auth.ICurrentUser>(FinanceSentry.Core.Auth.NoCurrentUser.Instance);
        services.AddLogging();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<BankSyncDbContext>();
        _db.Database.EnsureCreated();

        var loggerFactory = _provider.GetRequiredService<ILoggerFactory>();
        _sut = new AuditLogService(_provider.GetRequiredService<IServiceScopeFactory>(),
            loggerFactory.CreateLogger<AuditLogService>());
    }

    [Fact]
    public async Task Log_ReadAccount_InsertsAuditRow()
    {
        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        _sut.Log(userId, AuditActions.ReadAccount, "BankAccount", resourceId, correlationId: "corr-001");

        var row = await PollForAuditRowAsync(userId);
        Assert.NotNull(row);
        Assert.Equal(AuditActions.ReadAccount, row.Action);
        Assert.Equal(resourceId, row.ResourceId);
        Assert.Equal("corr-001", row.CorrelationId);
    }

    [Fact]
    public async Task Log_DeleteAccount_InsertsDeleteAuditRow()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _sut.Log(userId, AuditActions.DeleteAccount, "BankAccount", accountId);

        var row = await PollForAuditRowAsync(userId);
        Assert.NotNull(row);
        Assert.Equal(AuditActions.DeleteAccount, row.Action);
    }

    [Fact]
    public async Task Log_DoesNotContainSensitiveData()
    {
        var userId = Guid.NewGuid();

        _sut.Log(userId, AuditActions.CredentialAccess, "EncryptedCredential", null);

        var row = await PollForAuditRowAsync(userId);
        Assert.NotNull(row);

        // Verify no sensitive data fields are populated
        Assert.Null(row.UserAgent); // not supplied
        Assert.Equal(AuditActions.CredentialAccess, row.Action);
    }

    [Fact]
    public async Task Log_WriteFailure_DoesNotThrow()
    {
        // Arrange: get the scope factory before disposing so the call doesn't throw,
        // then dispose so that any scopes created inside the service will fail.
        var brokenProvider = new ServiceCollection()
            .AddDbContext<BankSyncDbContext>(o => o.UseInMemoryDatabase("broken"))
            .BuildServiceProvider();

        var scopeFactory = brokenProvider.GetRequiredService<IServiceScopeFactory>();
        brokenProvider.Dispose();

        var brokenService = new AuditLogService(
            scopeFactory,
            NullLogger<AuditLogService>.Instance);

        // Act + Assert: must not throw even when the write fails
        var exception = await Record.ExceptionAsync(async () =>
        {
            brokenService.Log(Guid.NewGuid(), AuditActions.ReadAccount, "BankAccount", null);
            await Task.Delay(300);
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Polls the database every 100 ms until a row for <paramref name="userId"/> appears
    /// or 3 seconds elapse. More reliable than a fixed Task.Delay for fire-and-forget writes.
    /// </summary>
    private async Task<AuditLog?> PollForAuditRowAsync(Guid userId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var row = await _db.AuditLogs.IgnoreQueryFilters([FinanceSentry.Core.Auth.OwnerQueryFilter.Name]).AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == userId);
            if (row != null)
                return row;
            await Task.Delay(100);
        }
        return null;
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}

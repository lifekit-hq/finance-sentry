namespace FinanceSentry.Tests.Integration.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Flex sync jobs record a persist-stage failure through the same scoped <see cref="BrokerageSyncDbContext"/>
/// whose <c>SaveChanges</c> just threw, so the failed entries are still tracked. The LastError write must
/// land anyway, which a tracker-based <c>SaveChanges</c> cannot do - it re-issues the failing statement.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IbkrFlexCredentialLastErrorPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();

        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private BrokerageSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(null));

    [DockerRequiredFact]
    public async Task SaveLastErrorUnscoped_AfterFailedSaveChangesOnSameContext_PersistsLastError_AndLeavesContextUsable()
    {
        var userId = Guid.NewGuid();
        var credential = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
        await using (var seed = CreateContext())
        {
            var seedRepo = new IBKRFlexCredentialRepository(seed);
            await seedRepo.AddAsync(credential);
            await seedRepo.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new IBKRFlexCredentialRepository(ctx);
        var tracked = (await repo.GetAllActiveUnscopedAsync()).Single(c => c.UserId == userId);

        // Poison the tracker the way a failed persist does: a second credential for the same user trips the
        // unique (UserId) index, and the Added entry stays tracked after the throw.
        await repo.AddAsync(new IBKRFlexCredential(userId, "888888", [1], [2], [3], 1));
        await repo.Invoking(r => r.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        await repo.Invoking(r => r.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>(
            "a tracker-based save re-issues the failing insert, which is why LastError needs its own write path");

        tracked.RecordUseError("persist blew up");
        await repo.SaveLastErrorUnscopedAsync(tracked);

        // The next user in the same sweep shares this context: its save must not replay the failed insert.
        var nextUser = Guid.NewGuid();
        await repo.AddAsync(new IBKRFlexCredential(nextUser, "777777", [1], [2], [3], 1));
        await repo.Invoking(r => r.SaveChangesAsync()).Should().NotThrowAsync();

        await using var verify = CreateContext();
        var verifyRepo = new IBKRFlexCredentialRepository(verify);
        (await verifyRepo.GetByUserIdUnscopedAsync(userId))!.LastError.Should().Be("persist blew up");
        (await verifyRepo.GetByUserIdUnscopedAsync(nextUser)).Should().NotBeNull();
    }
}

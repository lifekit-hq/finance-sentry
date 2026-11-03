namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>
/// M030 (release-readiness S2) re-keys the owner-specific system-default counterparties - the ones M011, M014,
/// M016 and M019 seeded with <c>UserId = Guid.Empty</c> - to the single Owner account, and leaves the generic
/// default (investment routing, M012) alone. The seeded rows are the real ones the earlier migrations insert, so
/// these tests identify them by id and never restate their contents.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RekeyOwnerCounterpartiesMigrationTests : IAsyncLifetime
{
    private const string BeforeMigration = "20261023120000_M029_CoverageHoles";
    private const string Migration = "20261103120000_M030_RekeyOwnerCounterparties";

    private static readonly Guid[] OwnerSpecificIds =
    [
        new("11111111-0000-0000-0000-000000000001"),
        new("11111111-0000-0000-0000-000000000002"),
        new("11111111-0000-0000-0000-000000000004"),
        new("11111111-0000-0000-0000-000000000005"),
        new("11111111-0000-0000-0000-000000000006")
    ];

    private static readonly Guid GenericId = new("11111111-0000-0000-0000-000000000003");

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var auth = CreateAuthContext();
        await auth.Database.MigrateAsync();
        await using var bankSync = CreateContext();
        await bankSync.GetService<IMigrator>().MigrateAsync(BeforeMigration);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private AuthDbContext CreateAuthContext() =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            NoCurrentUser.Instance);

    private BankSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            NoCurrentUser.Instance);

    private async Task<Guid> AddAccountAsync(string? role)
    {
        var id = Guid.NewGuid();
        var email = $"{id}@test.local";
        await using var auth = CreateAuthContext();
        auth.Users.Add(new ApplicationUser
        {
            Id = id.ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        });
        if (role is not null)
        {
            var roleRow = await auth.Roles.SingleOrDefaultAsync(r => r.NormalizedName == role.ToUpperInvariant());
            if (roleRow is null)
            {
                roleRow = new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() };
                auth.Roles.Add(roleRow);
            }
            auth.UserRoles.Add(new IdentityUserRole<string> { UserId = id.ToString(), RoleId = roleRow.Id });
        }
        await auth.SaveChangesAsync();
        return id;
    }

    private async Task MigrateAsync(string target)
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<Dictionary<Guid, Guid>> OwnerKeysAsync()
    {
        await using var db = CreateContext();
        return await db.Counterparties.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.UserId);
    }

    private async Task<int> RuleCountAsync()
    {
        await using var db = CreateContext();
        return await db.CounterpartyRules.AsNoTracking().CountAsync();
    }

    private async Task<long> BackupCountAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT to_regclass('bank_sync.counterparties_rekey_backup') IS NOT NULL", conn);
        if (!(bool)(await exists.ExecuteScalarAsync())!)
            return 0;
        await using var count = new NpgsqlCommand("SELECT count(*) FROM bank_sync.counterparties_rekey_backup", conn);
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private async Task<HashSet<Guid>> VisibleToAsync(Guid userId)
    {
        await using var db = CreateContext();
        var rows = await new CounterpartyRepository(db).GetForUserAsync(userId);
        return rows.Select(c => c.Id).ToHashSet();
    }

    [DockerRequiredFact]
    public async Task Migrate_WithOneOwner_ReKeysOnlyTheOwnerSpecificRows()
    {
        var owner = await AddAccountAsync(AuthRoles.Owner);
        await MigrateAsync(BeforeMigration);
        var rulesBefore = await RuleCountAsync();
        var keysBefore = await OwnerKeysAsync();
        keysBefore.Keys.Should().Contain([.. OwnerSpecificIds, GenericId]);
        keysBefore.Values.Should().OnlyContain(u => u == Guid.Empty, "every seeded counterparty starts as a system default");

        await MigrateAsync(Migration);

        var keys = await OwnerKeysAsync();
        foreach (var id in OwnerSpecificIds)
            keys[id].Should().Be(owner);
        keys[GenericId].Should().Be(Guid.Empty, "the generic default is not the owner's data");
        keys.Keys.Should().BeEquivalentTo(keysBefore.Keys, "no row is added or removed");
        (await RuleCountAsync()).Should().Be(rulesBefore, "rules ride on their counterparty and are not touched");
        (await BackupCountAsync()).Should().Be(OwnerSpecificIds.Length, "each touched row is copied before it changes");
    }

    [DockerRequiredFact]
    public async Task Migrate_OtherUsersNoLongerReceiveTheOwnersCounterparties_ButStillGetTheGenericDefault()
    {
        var owner = await AddAccountAsync(AuthRoles.Owner);
        var member = await AddAccountAsync(AuthRoles.Member);
        var roleless = await AddAccountAsync(role: null);

        await MigrateAsync(Migration);

        var forOwner = await VisibleToAsync(owner);
        forOwner.Should().BeEquivalentTo([.. OwnerSpecificIds, GenericId], "the owner keeps everything they had");

        foreach (var other in new[] { member, roleless, Guid.NewGuid() })
            (await VisibleToAsync(other)).Should().BeEquivalentTo([GenericId],
                "a user other than the owner only receives the generic default");
    }

    [DockerRequiredFact]
    public async Task Migrate_RunTwiceAndRolledBack_IsIdempotentAndRestoresTheOriginalKeys()
    {
        var owner = await AddAccountAsync(AuthRoles.Owner);
        await AddAccountAsync(AuthRoles.Member);
        await MigrateAsync(Migration);
        var afterFirst = await OwnerKeysAsync();

        await MigrateAsync(BeforeMigration);
        var rolledBack = await OwnerKeysAsync();
        rolledBack.Values.Should().OnlyContain(u => u == Guid.Empty, "Down restores the backed-up keys");

        await MigrateAsync(Migration);
        (await OwnerKeysAsync()).Should().BeEquivalentTo(afterFirst);
        (await OwnerKeysAsync())[OwnerSpecificIds[0]].Should().Be(owner);
        (await BackupCountAsync()).Should().Be(OwnerSpecificIds.Length, "a second run does not duplicate the backup");
    }

    [DockerRequiredFact]
    public async Task Migrate_LeavesRowsAlone_WhenThereIsNoOwner()
    {
        await AddAccountAsync(AuthRoles.Member);

        await MigrateAsync(Migration);

        (await OwnerKeysAsync()).Values.Should().OnlyContain(u => u == Guid.Empty);
        (await BackupCountAsync()).Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task Migrate_LeavesRowsAlone_WhenThereIsMoreThanOneOwner()
    {
        await AddAccountAsync(AuthRoles.Owner);
        await AddAccountAsync(AuthRoles.Owner);

        await MigrateAsync(Migration);

        (await OwnerKeysAsync()).Values.Should().OnlyContain(u => u == Guid.Empty, "it will not guess whose household this is");
        (await BackupCountAsync()).Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task Migrate_DoesNotTouchARowAlreadyKeyedToSomeone()
    {
        var owner = await AddAccountAsync(AuthRoles.Owner);
        var other = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            await db.Counterparties.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(c => c.Id == OwnerSpecificIds[0])
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, other));
        }

        await MigrateAsync(Migration);

        var keys = await OwnerKeysAsync();
        keys[OwnerSpecificIds[0]].Should().Be(other, "only rows still on the system-default key are re-keyed");
        keys[OwnerSpecificIds[1]].Should().Be(owner);
        (await BackupCountAsync()).Should().Be(OwnerSpecificIds.Length - 1);
    }
}

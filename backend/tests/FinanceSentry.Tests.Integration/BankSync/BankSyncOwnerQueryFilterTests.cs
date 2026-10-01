namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="BankSyncDbContext"/>: a context acting for one person sees only that
/// person's rows, a context with no person in scope sees none, and every background path that legitimately
/// reads across users (the sync repositories, the active-account snapshot, the retention sweep, the key
/// rotation, the shared-counterparty read) opts out explicitly, so none of them silently sees nothing.
/// Real Postgres is required, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankSyncOwnerQueryFilterTests : IAsyncLifetime
{
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private BankSyncDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private static BankAccount NewAccount(Guid userId) =>
        new(userId, $"ext-{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", "EUR", userId, "monobank");

    private static Transaction NewTransaction(BankAccount account, DateTime? postedDate = null) =>
        new(account.Id, account.UserId, -10m, DateTime.UtcNow, "coffee", Guid.NewGuid().ToString("N"))
        {
            PostedDate = postedDate ?? DateTime.UtcNow,
        };

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private async Task<List<Transaction>> AllTransactionsAsync()
    {
        await using var read = CreateContext();
        return await read.Transactions
            .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.ActiveFilterName]).AsNoTracking()
            .Where(t => t.UserId == _userA || t.UserId == _userB)
            .ToListAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().NotBeEmpty();
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_accounts_and_no_person_sees_none()
    {
        var a = NewAccount(_userA);
        var b = NewAccount(_userB);
        await SeedAsync(a, b);

        await using (var asA = CreateContext(_userA))
            (await asA.BankAccounts.Select(x => x.Id).ToListAsync()).Should().Equal(a.Id);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.BankAccounts.Select(x => x.Id).ToListAsync()).Should().Equal(b.Id);
            (await asB.BankAccounts.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.BankAccounts.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_transactions_and_archived_rows_stay_hidden()
    {
        var accountA = NewAccount(_userA);
        var accountB = NewAccount(_userB);
        var txA = NewTransaction(accountA);
        var txB = NewTransaction(accountB);
        var archivedA = NewTransaction(accountA);
        archivedA.IsActive = false;
        await SeedAsync(accountA, accountB, txA, txB, archivedA);

        await using (var asA = CreateContext(_userA))
        {
            (await asA.Transactions.Select(x => x.Id).ToListAsync()).Should().Equal(txA.Id);
            (await asA.Transactions.IgnoreQueryFilters([BankSyncDbContext.ActiveFilterName]).CountAsync())
                .Should().Be(2, "opting out of the soft-delete filter must not lift the owner scope");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Transactions.AnyAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Counterparty_reader_with_no_person_reads_the_users_rows_and_the_shared_ones_only()
    {
        var shared = new Counterparty { UserId = Guid.Empty, Name = "Shared", FlowRole = FlowRoles.FamilySupport };
        var mine = new Counterparty { UserId = _userA, Name = "Mine", FlowRole = FlowRoles.FamilySupport };
        var theirs = new Counterparty { UserId = _userB, Name = "Theirs", FlowRole = FlowRoles.FamilySupport };
        await SeedAsync(shared, mine, theirs);

        await using var ctx = CreateContext();
        var rows = await new CounterpartyRepository(ctx).GetForUserAsync(_userA);

        rows.Select(r => r.Id).Should().BeEquivalentTo([shared.Id, mine.Id]);
    }

    [DockerRequiredFact]
    public async Task Sync_repositories_with_no_person_read_and_write_the_named_accounts_and_users_rows()
    {
        var accountA = NewAccount(_userA);
        var accountB = NewAccount(_userB);
        var txA = NewTransaction(accountA);
        await SeedAsync(accountA, accountB, txA);

        await using var ctx = CreateContext();
        var accounts = new BankAccountRepository(ctx);
        var transactions = new TransactionRepository(ctx);

        (await accounts.GetByIdAsync(accountA.Id)).Should().NotBeNull();
        (await accounts.ExistsByExternalAccountIdAsync(accountB.ExternalAccountId)).Should().BeTrue(
            "external account ids are unique across users, so the duplicate check sees every user");
        (await accounts.GetByUserIdAsync(_userA)).Select(x => x.Id).Should().Equal(accountA.Id);
        (await accounts.GetAllActiveAsync()).Select(x => x.Id).Should().BeEquivalentTo([accountA.Id, accountB.Id]);
        (await transactions.GetByAccountIdAsync(accountA.Id)).Select(x => x.Id).Should().Equal(txA.Id);
    }

    [DockerRequiredFact]
    public async Task Active_account_snapshot_with_no_person_covers_every_users_accounts()
    {
        var accountA = NewAccount(_userA);
        var accountB = NewAccount(_userB);
        await SeedAsync(accountA, accountB);

        await using var ctx = CreateContext();
        var snapshot = await ActiveAccountSnapshot.ReadAsync(ctx, default);

        snapshot.AccountIds.Should().Contain([accountA.Id, accountB.Id]);
    }

    [DockerRequiredFact]
    public async Task Retention_job_with_no_person_archives_old_transactions_of_every_user()
    {
        var old = DateTime.UtcNow.AddMonths(-30);
        var accountA = NewAccount(_userA);
        var accountB = NewAccount(_userB);
        await SeedAsync(accountA, accountB, NewTransaction(accountA, old), NewTransaction(accountB, old));

        await using (var ctx = CreateContext())
            await new DataRetentionJob(ctx, NullLogger<DataRetentionJob>.Instance).RunAsync();

        (await AllTransactionsAsync()).Should().HaveCount(2).And.OnlyContain(t => !t.IsActive);
    }

    [DockerRequiredFact]
    public async Task Rotation_target_with_no_person_rotates_every_users_credentials()
    {
        var encryption = new Mock<ICredentialEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>())).Returns("t");
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns(new EncryptionResult([2], [2], [2], 2));
        await SeedAsync(
            new MonobankCredential(_userA, [1], [1], [1], 1),
            new MonobankCredential(_userB, [1], [1], [1], 1));

        await using var ctx = CreateContext();
        var rotated = await new MonobankCredentialRotationTarget(ctx, encryption.Object).RotateAsync(2, default);

        rotated.Should().Be(2);
    }
}

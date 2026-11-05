namespace FinanceSentry.Tests.Unit.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Auditing;
using FinanceSentry.Infrastructure.Encryption;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Writes to entities marked <see cref="AuditedProfileDataAttribute"/> reach the audit trail with the entity's id and the
/// NAMES of the fields written, before the save commits, and nothing of their values. The entity here stands in for the
/// declared income and career rows the later slices add: encrypted amount and label, owned by one person.
/// </summary>
public sealed class AuditedWriteSaveChangesInterceptorTests
{
    // Values that must never appear on the trail, or in the stored column.
    private const string SecretLabel = "Zebra-Corp-severance";
    private const decimal SecretAmount = 73421.17m;

    private static readonly CredentialEncryptionService Encryption = new(Options.Create(new EncryptionOptions
    {
        CurrentKeyVersion = 1,
        Keys = new Dictionary<int, string> { [1] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=" },
    }));

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Guid _person = Guid.NewGuid();
    private readonly RecordingTrail _trail = new();

    private ProfileDbContext CreateContext(AuditActorKind kind = AuditActorKind.OwnerSession, Guid? person = null, IAuditTrail? trail = null)
    {
        var actorPerson = person ?? _person;
        var actor = new AuditActor(new FixedUser(actorPerson), () => kind);
        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(new AuditedWriteSaveChangesInterceptor(trail ?? _trail, actor))
            .Options;
        return new ProfileDbContext(options);
    }

    private async Task<Guid> SeedAsync()
    {
        await using var db = CreateContext();
        var row = new IncomeRow { UserId = _person, Label = SecretLabel, Amount = SecretAmount, Cadence = "monthly" };
        db.Incomes.Add(row);
        await db.SaveChangesAsync();
        _trail.Entries.Clear();
        return row.Id;
    }

    [Fact]
    public async Task ANewRow_IsRecordedAsACreate_WithItsIdAndEveryFieldName()
    {
        await using var db = CreateContext();
        var row = new IncomeRow { UserId = _person, Label = SecretLabel, Amount = SecretAmount, Cadence = "monthly" };
        db.Incomes.Add(row);

        await db.SaveChangesAsync();

        var entry = _trail.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.Create);
        entry.ResourceType.Should().Be("income_source");
        entry.ResourceId.Should().Be(row.Id);
        entry.SubjectUserId.Should().Be(_person);
        entry.ActorUserId.Should().Be(_person);
        entry.ActorKind.Should().Be(AuditActorKind.OwnerSession);
        entry.FieldNames.Should().Equal("Amount", "Cadence", "Label");
    }

    [Fact]
    public async Task AnEdit_RecordsOnlyTheFieldsWhoseValueChanged()
    {
        var id = await SeedAsync();

        await using var db = CreateContext();
        (await db.Incomes.SingleAsync(x => x.Id == id)).Amount = SecretAmount + 1;
        await db.SaveChangesAsync();

        var entry = _trail.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.Update);
        entry.ResourceId.Should().Be(id);
        entry.FieldNames.Should().Equal("Amount");
    }

    [Fact]
    public async Task AReSaveThatChangesNothing_RecordsNothing()
    {
        var id = await SeedAsync();

        await using var db = CreateContext();
        var row = await db.Incomes.SingleAsync(x => x.Id == id);
        db.Entry(row).Property(x => x.Amount).IsModified = true; // what a key rotation does
        await db.SaveChangesAsync();

        _trail.Entries.Should().BeEmpty("rewriting the same value wrote no new fact");
    }

    [Fact]
    public async Task ADelete_IsRecorded_WithNoFieldNames()
    {
        var id = await SeedAsync();

        await using var db = CreateContext();
        db.Incomes.Remove(await db.Incomes.SingleAsync(x => x.Id == id));
        await db.SaveChangesAsync();

        var entry = _trail.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.Delete);
        entry.ResourceId.Should().Be(id);
        entry.FieldNames.Should().BeEmpty();
    }

    [Fact]
    public async Task SeveralRowsInOneSave_AreRecordedTogether_OnePerRow()
    {
        await using var db = CreateContext();
        db.Incomes.AddRange(
            new IncomeRow { UserId = _person, Label = "a", Amount = 1m, Cadence = "monthly" },
            new IncomeRow { UserId = _person, Label = "b", Amount = 2m, Cadence = "annual" });

        await db.SaveChangesAsync();

        _trail.Entries.Should().HaveCount(2);
        _trail.Entries.Select(e => e.ResourceId).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task AnEntityNotMarkedAsProfileData_IsNotRecorded()
    {
        await using var db = CreateContext();
        db.Plain.Add(new PlainRow { UserId = _person, Name = "not profile data" });

        await db.SaveChangesAsync();

        _trail.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task NoValueReachesTheTrail()
    {
        var id = await SeedAsync();
        await using (var db = CreateContext())
        {
            db.Incomes.Add(new IncomeRow { UserId = _person, Label = SecretLabel, Amount = SecretAmount, Cadence = "monthly" });
            (await db.Incomes.SingleAsync(x => x.Id == id)).Label = SecretLabel + "-edited";
            await db.SaveChangesAsync();
        }

        var everything = JsonSerializer.Serialize(_trail.Entries);
        everything.Should().NotContain("Zebra").And.NotContain("73421").And.NotContain("severance");
    }

    [Fact]
    public async Task TheStoredColumn_IsCiphertext_AndReadsBackAsThePlaintext()
    {
        var id = await SeedAsync();

        var stored = (string)new EncryptedDecimalConverter(Encryption).ConvertToProvider(SecretAmount)!;
        stored.Should().StartWith("v1.").And.NotContain("73421");

        await using var db = CreateContext();
        var row = await db.Incomes.SingleAsync(x => x.Id == id);
        row.Amount.Should().Be(SecretAmount);
        row.Label.Should().Be(SecretLabel);
    }

    [Fact]
    public async Task TheActorKind_IsWhatTheHostSaid_AndAJobWhenNoPersonIsInScope()
    {
        await using (var db = CreateContext(AuditActorKind.InAppAgent))
        {
            db.Incomes.Add(new IncomeRow { UserId = _person, Label = "x", Amount = 1m, Cadence = "monthly" });
            await db.SaveChangesAsync();
        }

        _trail.Entries.Single().ActorKind.Should().Be(AuditActorKind.InAppAgent);
        _trail.Entries.Clear();

        var job = new AuditActor(new FixedUser(null), () => AuditActorKind.OwnerSession);
        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(new AuditedWriteSaveChangesInterceptor(_trail, job))
            .Options;
        await using (var db = new ProfileDbContext(options))
        {
            db.Incomes.Add(new IncomeRow { UserId = _person, Label = "y", Amount = 2m, Cadence = "monthly" });
            await db.SaveChangesAsync();
        }

        var entry = _trail.Entries.Should().ContainSingle().Subject;
        entry.ActorKind.Should().Be(AuditActorKind.Job);
        entry.ActorUserId.Should().BeNull();
        entry.SubjectUserId.Should().Be(_person, "the trail names whose data it was even when no person acted");
    }

    [Fact]
    public async Task WhenTheTrailCannotBeWritten_TheWriteDoesNotHappen()
    {
        await using (var db = CreateContext(trail: new ThrowingTrail()))
        {
            db.Incomes.Add(new IncomeRow { UserId = _person, Label = "lost", Amount = 1m, Cadence = "monthly" });

            var act = () => db.SaveChangesAsync();

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*audit store is down*");
        }

        await using var verify = CreateContext();
        (await verify.Incomes.CountAsync()).Should().Be(0, "a write with no trail entry is refused, not committed");
    }

    [Fact]
    public async Task AMarkedEntityThatIsNotOwned_IsRefused()
    {
        var options = new DbContextOptionsBuilder<UnownedDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(new AuditedWriteSaveChangesInterceptor(_trail, new AuditActor(new FixedUser(_person), () => AuditActorKind.OwnerSession)))
            .Options;
        await using var db = new UnownedDbContext(options);
        db.Rows.Add(new UnownedRow());

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not IUserOwned*");
    }

    [Fact]
    public async Task TheSynchronousSave_IsAuditedToo()
    {
        await using var db = CreateContext();
        db.Incomes.Add(new IncomeRow { UserId = _person, Label = "sync", Amount = 3m, Cadence = "monthly" });

        db.SaveChanges();

        _trail.Entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.Create);
    }

    private sealed class FixedUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default)
        {
            Entries.AddRange(entries);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTrail : IAuditTrail
    {
        public Task RecordAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the audit store is down");
    }

    [AuditedProfileData("income_source")]
    private sealed class IncomeRow : IUserOwned
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Cadence { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class PlainRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [AuditedProfileData("unowned")]
    private sealed class UnownedRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
    {
        public DbSet<IncomeRow> Incomes => Set<IncomeRow>();
        public DbSet<PlainRow> Plain => Set<PlainRow>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<IncomeRow>(e =>
            {
                e.Property(x => x.Label).IsEncrypted(Encryption);
                e.Property(x => x.Amount).IsEncrypted(Encryption);
            });
        }
    }

    private sealed class UnownedDbContext(DbContextOptions<UnownedDbContext> options) : DbContext(options)
    {
        public DbSet<UnownedRow> Rows => Set<UnownedRow>();
    }
}

namespace FinanceSentry.Tests.Integration.Shared;

using System.Text;
using System.Text.Json;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Auditing;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Auth;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

/// <summary>
/// Field encryption (S3 primitive) end to end against real Postgres, through a stand-in for the profile entities the
/// later slices add: encrypted columns that are plain text in code, an owner, and <c>[AuditedProfileData]</c>.
/// Pins what the slice promises: the column holds ciphertext, key rotation re-seals every row and reads back, the
/// person's data download carries the decrypted value, erasure removes it, and a write reaches the real audit trail.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EncryptedFieldPersistenceTests : IAsyncLifetime
{
    private const string KeyV1 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyV2 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    private const string SecretLabel = "Zebra-Corp-signing-bonus";
    private const decimal SecretAmount = 88123.45m;

    // The service of a process whose current key is v1, and of one a deploy later whose current key is v2.
    private static readonly CredentialEncryptionService Before = Service(1);
    private static readonly CredentialEncryptionService After = Service(2);

    private readonly Guid _personA = Guid.NewGuid();
    private readonly Guid _personB = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        await using var auth = new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_database.ConnectionString).Options, NoCurrentUser.Instance);
        await auth.Database.EnsureCreatedAsync();
        await using var profile = Profile(Before);
        await profile.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private static CredentialEncryptionService Service(int current) =>
        new(Options.Create(new EncryptionOptions
        {
            CurrentKeyVersion = current,
            Keys = new Dictionary<int, string> { [1] = KeyV1, [2] = KeyV2 },
        }));

    private ProfileDbContext Profile(ICredentialEncryptionService encryption, IAuditTrail? trail = null, Guid? person = null)
    {
        var builder = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseNpgsql(_database!.ConnectionString)
            // EF caches the model per context type, and the converter binds the encryption service when the model is
            // built. One process has one service; this test plays two processes, so the cache key carries the service.
            .ReplaceService<IModelCacheKeyFactory, PerEncryptionModelCacheKeyFactory>();
        if (trail is not null)
        {
            var actor = new AuditActor(new FixedCurrentUser(person ?? _personA), () => AuditActorKind.OwnerSession);
            builder.AddInterceptors(new AuditedWriteSaveChangesInterceptor(trail, actor));
        }

        return new ProfileDbContext(builder.Options, encryption);
    }

    private async Task<Guid> InsertAsync(Guid owner, ICredentialEncryptionService encryption)
    {
        await using var db = Profile(encryption);
        var row = new IncomeRow { UserId = owner, Label = SecretLabel, Amount = SecretAmount, Note = "kept" };
        db.Incomes.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<(string Label, string Amount, string? Note)> RawColumnsAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"Label\", \"Amount\", \"Note\" FROM profile_test.incomes WHERE \"Id\" = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    [DockerRequiredFact]
    public async Task TheStoredColumns_AreCiphertext_AndReadBackAsPlaintext()
    {
        var id = await InsertAsync(_personA, Before);

        var (label, amount, note) = await RawColumnsAsync(id);
        label.Should().StartWith("v1.").And.NotContain("Zebra");
        amount.Should().StartWith("v1.").And.NotContain("88123");
        note.Should().Be("kept", "a column without .IsEncrypted stays plain");

        await using var db = Profile(Before);
        var row = await db.Incomes.SingleAsync(x => x.Id == id);
        row.Label.Should().Be(SecretLabel);
        row.Amount.Should().Be(SecretAmount);
    }

    [DockerRequiredFact]
    public async Task Rotation_ReSealsEveryRowUnderTheCurrentKey_AndTheValuesReadBack()
    {
        var a = await InsertAsync(_personA, Before);
        var b = await InsertAsync(_personB, Before);
        (await RawColumnsAsync(a)).Label.Should().StartWith("v1.");

        await using (var db = Profile(After))
        {
            var target = new EncryptedFieldRotationTarget<ProfileDbContext>(db, "ProfileTest");

            (await target.RotateAsync(2, default)).Should().Be(2, "two rows were on the old key, however many encrypted columns each has");
        }

        foreach (var id in new[] { a, b })
        {
            var (label, amount, _) = await RawColumnsAsync(id);
            label.Should().StartWith("v2.");
            amount.Should().StartWith("v2.");
        }

        await using var read = Profile(After);
        var rows = await read.Incomes.OrderBy(x => x.UserId).ToListAsync();
        rows.Should().OnlyContain(r => r.Label == SecretLabel && r.Amount == SecretAmount);
    }

    [DockerRequiredFact]
    public async Task Rotation_IsIdempotent_ASecondRunFindsNothing()
    {
        await InsertAsync(_personA, Before);
        await using var db = Profile(After);
        var target = new EncryptedFieldRotationTarget<ProfileDbContext>(db, "ProfileTest");

        await target.RotateAsync(2, default);

        (await target.RotateAsync(2, default)).Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task Rotation_ThroughTheRealService_CoversAContextRegisteredByOneLine()
    {
        var id = await InsertAsync(_personA, Before);
        var services = new ServiceCollection();
        services.AddScoped(_ => Profile(After));
        services.AddEncryptedFieldRotation<ProfileDbContext>("ProfileTest");
        services.AddSingleton(Options.Create(new EncryptionOptions { CurrentKeyVersion = 2, Keys = new Dictionary<int, string> { [1] = KeyV1, [2] = KeyV2 } }));
        services.AddLogging();
        services.AddScoped<CredentialKeyRotationService>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var migrated = await scope.ServiceProvider.GetRequiredService<CredentialKeyRotationService>().RotateAllAsync();

        migrated["ProfileTest"].Should().Be(1);
        (await RawColumnsAsync(id)).Label.Should().StartWith("v2.");
    }

    [DockerRequiredFact]
    public async Task TheDataExport_CarriesTheDecryptedValue_ForTheOwnerOnly()
    {
        await InsertAsync(_personA, Before);
        await InsertAsync(_personB, Before);

        await using var db = Profile(Before);
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            await OwnedRowExporter.ExportAsync(db, _personA, writer);
            writer.WriteEndObject();
        }

        var text = Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(text);
        var rows = document.RootElement.GetProperty("incomes");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("label").GetString().Should().Be(SecretLabel, "an encrypted text column is the person's own data, exported decrypted");
        rows[0].GetProperty("amount").GetDecimal().Should().Be(SecretAmount);
        text.Should().NotContain("v1.", "no ciphertext leaks into the export");
        text.Should().NotContain(_personB.ToString());
    }

    [DockerRequiredFact]
    public async Task Erasure_RemovesThePersonsEncryptedRows_AndOnlyTheirs()
    {
        await InsertAsync(_personA, Before);
        var keep = await InsertAsync(_personB, Before);

        await using (var db = Profile(Before))
            (await OwnedRowEraser.EraseAsync(db, _personA)).Should().Be(1);

        await using var verify = Profile(Before);
        (await verify.Incomes.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).Should().Equal(keep);
    }

    [DockerRequiredFact]
    public async Task AWrite_ReachesTheRealAuditTrail_WithFieldNamesAndNoValues()
    {
        await using var services = AuthServices();
        using var scope = services.CreateScope();
        var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

        Guid id;
        await using (var db = Profile(Before, trail))
        {
            var row = new IncomeRow { UserId = _personA, Label = SecretLabel, Amount = SecretAmount, Note = "n" };
            db.Incomes.Add(row);
            await db.SaveChangesAsync();
            id = row.Id;
        }

        await using (var db = Profile(Before, trail))
        {
            (await db.Incomes.SingleAsync(x => x.Id == id)).Amount = SecretAmount + 1;
            await db.SaveChangesAsync();
        }

        await using var auth = new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_database!.ConnectionString).Options, new FixedCurrentUser(_personA));
        var events = await auth.AuditEvents.OrderBy(e => e.OccurredAt).ToListAsync();
        events.Select(e => e.Action).Should().Equal(AuditAction.Create, AuditAction.Update);
        events.Should().OnlyContain(e => e.ResourceId == id && e.ResourceType == "income_source" && e.SubjectUserId == _personA);
        events[0].FieldNames.Should().Equal("Amount", "Label", "Note");
        events[1].FieldNames.Should().Equal("Amount");

        await using var conn = new NpgsqlConnection(_database.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT string_agg(t::text, ' ') FROM auth.audit_events t", conn);
        var dump = (string?)await cmd.ExecuteScalarAsync();
        dump.Should().NotContain("Zebra").And.NotContain("88123").And.NotContain("88124", "the audit table holds no value, plain or sealed");
    }

    private ServiceProvider AuthServices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _database!.ConnectionString,
            ["Jwt:Secret"] = "encrypted-field-tests-signing-secret-0123456789-abcdef",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICurrentUser>(new FixedCurrentUser(_personA));
        services.AddAuthModule(config);
        return services.BuildServiceProvider();
    }

    public sealed class PerEncryptionModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((ProfileDbContext)context).Encryption, designTime);
    }

    [AuditedProfileData("income_source")]
    public sealed class IncomeRow : IUserOwned
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options, ICredentialEncryptionService encryption)
        : DbContext(options)
    {
        public ICredentialEncryptionService Encryption { get; } = encryption;

        public DbSet<IncomeRow> Incomes => Set<IncomeRow>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.HasDefaultSchema("profile_test");
            builder.Entity<IncomeRow>(e =>
            {
                e.ToTable("incomes");
                e.Property(x => x.Label).IsEncrypted(Encryption);
                e.Property(x => x.Amount).IsEncrypted(Encryption);
            });
        }
    }
}

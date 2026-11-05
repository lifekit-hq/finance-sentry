namespace FinanceSentry.Tests.Integration.Wealth;

using System.Text;
using System.Text.Json;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Infrastructure.Auditing;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

/// <summary>
/// Declared income (#454 S4) against real Postgres through the real <see cref="WealthDbContext"/>: the label and amount
/// are ciphertext in their columns, key rotation re-seals them, the data download carries them decrypted and the erasure
/// removes them, a write reaches the audit trail by field name only, and a host that holds no key can read the plain
/// columns and nothing else. Synthetic values throughout.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IncomeSourcePersistenceTests : IAsyncLifetime
{
    private const string KeyV1 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyV2 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    private const string SecretLabel = "Zebra-Corp-retainer";
    private const decimal SecretAmount = 6543.21m;

    private static readonly CredentialEncryptionService Before = Service(1);
    private static readonly CredentialEncryptionService After = Service(2);

    private readonly Guid _personA = Guid.NewGuid();
    private readonly Guid _personB = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        await using var setup = Context(Before, _personA);
        await setup.Database.EnsureCreatedAsync();
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

    private WealthDbContext Context(ICredentialEncryptionService? encryption, Guid? person, IAuditTrail? trail = null)
    {
        var builder = new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString);
        if (trail is not null)
        {
            var actor = new AuditActor(new FixedCurrentUser(person), () => AuditActorKind.OwnerSession);
            builder.AddInterceptors(new AuditedWriteSaveChangesInterceptor(trail, actor));
        }

        return new WealthDbContext(builder.Options, new FixedCurrentUser(person), encryption);
    }

    private static IncomeSource NewSource(Guid owner, IncomeCadence cadence = IncomeCadence.Monthly)
        => IncomeSource.Create(
            owner, IncomeKind.Side, SecretLabel, SecretAmount, "EUR", cadence, IncomeBasis.Net, new DateOnly(2026, 1, 1), null);

    private async Task<Guid> InsertAsync(Guid owner, ICredentialEncryptionService encryption, IAuditTrail? trail = null)
    {
        await using var db = Context(encryption, owner, trail);
        var source = NewSource(owner);
        await new IncomeSourceRepository(db).AddAsync(source);
        return source.Id;
    }

    private async Task<(string Label, string Amount, string Kind, string Currency)> RawAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT label, amount, kind, currency FROM income_sources WHERE \"Id\" = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    [DockerRequiredFact]
    public async Task TheLabelAndAmount_AreCiphertextInTheirColumns_AndReadBackAsPlaintext()
    {
        var id = await InsertAsync(_personA, Before);

        var (label, amount, kind, currency) = await RawAsync(id);
        label.Should().StartWith("v1.").And.NotContain("Zebra");
        amount.Should().StartWith("v1.").And.NotContain("6543");
        (kind, currency).Should().Be(("Side", "EUR"), "the shape of the income is not a secret");

        await using var db = Context(Before, _personA);
        var row = await new IncomeSourceRepository(db).GetAsync(_personA, id);
        row!.Label.Should().Be(SecretLabel);
        row.Amount.Should().Be(SecretAmount);
    }

    [DockerRequiredFact]
    public async Task Rotation_ReSealsTheIncomeUnderTheCurrentKey_AndItReadsBack()
    {
        var id = await InsertAsync(_personA, Before);

        await using (var db = Context(After, null))
            (await new EncryptedFieldRotationTarget<WealthDbContext>(db, "wealth.income_sources").RotateAsync(2, default)).Should().Be(1);

        var (label, amount, _, _) = await RawAsync(id);
        label.Should().StartWith("v2.");
        amount.Should().StartWith("v2.");

        await using var read = Context(After, _personA);
        var row = await new IncomeSourceRepository(read).GetAsync(_personA, id);
        (row!.Label, row.Amount).Should().Be((SecretLabel, SecretAmount));
    }

    [DockerRequiredFact]
    public async Task TheDataExport_CarriesTheDecryptedIncome_ForTheOwnerOnly()
    {
        await InsertAsync(_personA, Before);
        await InsertAsync(_personB, Before);

        await using var db = Context(Before, null);
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            await OwnedRowExporter.ExportAsync(db, _personA, writer);
            writer.WriteEndObject();
        }

        var text = Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(text);
        var rows = document.RootElement.GetProperty("income_sources");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("label").GetString().Should().Be(SecretLabel);
        rows[0].GetProperty("amount").GetDecimal().Should().Be(SecretAmount);
        text.Should().NotContain("v1.", "no ciphertext leaks into the export");
        text.Should().NotContain(_personB.ToString());
    }

    [DockerRequiredFact]
    public async Task Erasure_RemovesThePersonsIncome_AndOnlyTheirs()
    {
        await InsertAsync(_personA, Before);
        var keep = await InsertAsync(_personB, Before);

        await using (var db = Context(Before, null))
            (await OwnedRowEraser.EraseAsync(db, _personA)).Should().Be(1);

        await using var verify = Context(Before, null);
        (await verify.IncomeSources.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).Should().Equal(keep);
    }

    [DockerRequiredFact]
    public async Task EveryWrite_IsOnTheAuditTrail_ByFieldNameAndNeverByValue()
    {
        var trail = new RecordingTrail();
        var id = await InsertAsync(_personA, Before, trail);

        await using (var db = Context(Before, _personA, trail))
        {
            var repository = new IncomeSourceRepository(db);
            var row = (await repository.GetAsync(_personA, id))!;
            row.Update(IncomeKind.Side, SecretLabel, SecretAmount + 1m, "EUR", IncomeCadence.Monthly, IncomeBasis.Net, new DateOnly(2026, 1, 1), null);
            await repository.SaveAsync();
            row.Retire(new DateOnly(2026, 11, 5));
            await repository.SaveAsync();
            await repository.RemoveAsync(row);
        }

        trail.Entries.Select(e => e.Action).Should().Equal(AuditAction.Create, AuditAction.Update, AuditAction.Update, AuditAction.Delete);
        trail.Entries.Should().OnlyContain(e => e.ResourceType == "income_source" && e.ResourceId == id && e.SubjectUserId == _personA);
        trail.Entries[0].FieldNames.Should().Equal("Amount", "Cadence", "Currency", "EndDate", "GrossOrNet", "Kind", "Label", "StartDate");
        trail.Entries[1].FieldNames.Should().Equal("Amount");
        trail.Entries[2].FieldNames.Should().Equal("EndDate");
        trail.Entries[3].FieldNames.Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task AHostWithoutTheKey_ReadsThePlainColumns_AndCannotReadOrWriteTheEncryptedOnes()
    {
        var id = await InsertAsync(_personA, Before);

        await using var keyless = Context(null, _personA);
        var repository = new IncomeSourceRepository(keyless);
        repository.CanReadEncryptedFields.Should().BeFalse();

        var outline = (await repository.ListOutlinesAsync(_personA)).Should().ContainSingle().Subject;
        (outline.Id, outline.Kind, outline.Currency, outline.Cadence, outline.GrossOrNet)
            .Should().Be((id, IncomeKind.Side, "EUR", IncomeCadence.Monthly, IncomeBasis.Net));

        var read = async () => await repository.ListAsync(_personA);
        await read.Should().ThrowAsync<FieldKeyUnavailableException>();

        var write = async () => await repository.AddAsync(NewSource(_personA));
        (await write.Should().ThrowAsync<DbUpdateException>()).WithInnerException<FieldKeyUnavailableException>();
    }

    [DockerRequiredFact]
    public async Task AKeylessModelBuiltFirst_DoesNotPoisonAKeyedContextInTheSameProcess()
    {
        await using (var keyless = Context(null, _personA))
            _ = await new IncomeSourceRepository(keyless).ListOutlinesAsync(_personA);

        var id = await InsertAsync(_personA, Before);

        await using var keyed = Context(Before, _personA);
        (await new IncomeSourceRepository(keyed).GetAsync(_personA, id))!.Label.Should().Be(SecretLabel);
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
}

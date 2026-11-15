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
/// The stated tax profile (#454) against real Postgres through the real <see cref="WealthDbContext"/>: the notes and caveat
/// are ciphertext in their columns, key rotation re-seals them, the data download carries them decrypted and the erasure
/// removes them, a write reaches the audit trail by field name only, one row per person, and a host that holds no key can
/// read the code and the has-text flags and nothing else. Synthetic values throughout.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TaxProfilePersistenceTests : IAsyncLifetime
{
    private const string KeyV1 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyV2 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    private const string SecretNotes = "Zebra-notes-PAYE-and-RSU";
    private const string SecretCaveat = "Zebra-caveat-check-with-adviser";

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

    private static TaxProfile NewProfile(Guid owner, string? caveat = SecretCaveat)
        => TaxProfile.Create(owner, "IE", SecretNotes, caveat);

    private async Task<Guid> InsertAsync(Guid owner, ICredentialEncryptionService encryption, IAuditTrail? trail = null, string? caveat = SecretCaveat)
    {
        await using var db = Context(encryption, owner, trail);
        var profile = NewProfile(owner, caveat);
        await new TaxProfileRepository(db).AddAsync(profile);
        return profile.Id;
    }

    private async Task<(string Jurisdiction, string? Notes, string? Caveat)> RawAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT jurisdiction, jurisdiction_notes, caveat FROM tax_profiles WHERE \"Id\" = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    [DockerRequiredFact]
    public async Task TheNotesAndCaveat_AreCiphertextInTheirColumns_AndReadBackAsPlaintext()
    {
        var id = await InsertAsync(_personA, Before);

        var (jurisdiction, notes, caveat) = await RawAsync(id);
        jurisdiction.Should().Be("IE", "the code is a shape, not a secret");
        notes.Should().StartWith("v1.").And.NotContain("Zebra");
        caveat.Should().StartWith("v1.").And.NotContain("Zebra");

        await using var db = Context(Before, _personA);
        var row = await new TaxProfileRepository(db).GetAsync(_personA);
        (row!.JurisdictionNotes, row.Caveat).Should().Be((SecretNotes, SecretCaveat));
    }

    [DockerRequiredFact]
    public async Task ANullField_IsStoredAsNull_NotAsCiphertextOfNothing()
    {
        var id = await InsertAsync(_personA, Before, caveat: null);

        var (_, notes, caveat) = await RawAsync(id);

        notes.Should().StartWith("v1.");
        caveat.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task APersonHasOneProfile_TheDatabaseRefusesASecond()
    {
        await InsertAsync(_personA, Before);

        var second = async () => await InsertAsync(_personA, Before);

        await second.Should().ThrowAsync<DbUpdateException>();
    }

    [DockerRequiredFact]
    public async Task Rotation_ReSealsTheProfileUnderTheCurrentKey_AndItReadsBack()
    {
        var id = await InsertAsync(_personA, Before);

        await using (var db = Context(After, null))
            (await new EncryptedFieldRotationTarget<WealthDbContext>(db, "wealth.income_sources").RotateAsync(2, default)).Should().BeGreaterThanOrEqualTo(1);

        var (_, notes, caveat) = await RawAsync(id);
        notes.Should().StartWith("v2.");
        caveat.Should().StartWith("v2.");

        await using var read = Context(After, _personA);
        var row = await new TaxProfileRepository(read).GetAsync(_personA);
        (row!.JurisdictionNotes, row.Caveat).Should().Be((SecretNotes, SecretCaveat));
    }

    [DockerRequiredFact]
    public async Task TheDataExport_CarriesTheDecryptedProfile_ForTheOwnerOnly()
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
        var rows = document.RootElement.GetProperty("tax_profiles");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("jurisdiction_notes").GetString().Should().Be(SecretNotes);
        rows[0].GetProperty("caveat").GetString().Should().Be(SecretCaveat);
        text.Should().NotContain("v1.", "no ciphertext leaks into the export");
        text.Should().NotContain(_personB.ToString());
    }

    [DockerRequiredFact]
    public async Task Erasure_RemovesThePersonsProfile_AndOnlyTheirs()
    {
        await InsertAsync(_personA, Before);
        var keep = await InsertAsync(_personB, Before);

        await using (var db = Context(Before, null))
            (await OwnedRowEraser.EraseAsync(db, _personA)).Should().Be(1);

        await using var verify = Context(Before, null);
        (await verify.TaxProfiles.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).Should().Equal(keep);
    }

    [DockerRequiredFact]
    public async Task TheOwnerFilter_HidesAnotherPersonsProfile_AndAJobWithNoPrincipalSeesNone()
    {
        await InsertAsync(_personA, Before);

        await using var asB = Context(Before, _personB);
        (await new TaxProfileRepository(asB).GetAsync(_personA)).Should().BeNull();

        await using var noPrincipal = Context(Before, null);
        (await noPrincipal.TaxProfiles.CountAsync()).Should().Be(0);
    }

    [DockerRequiredFact]
    public async Task EveryWrite_IsOnTheAuditTrail_ByFieldNameAndNeverByValue()
    {
        var trail = new RecordingTrail();
        var id = await InsertAsync(_personA, Before, trail);

        await using (var db = Context(Before, _personA, trail))
        {
            var repository = new TaxProfileRepository(db);
            var row = (await repository.GetAsync(_personA))!;
            row.Update("IE", SecretNotes, null);
            await repository.SaveAsync();
            await repository.RemoveAsync(row);
        }

        trail.Entries.Select(e => e.Action).Should().Equal(AuditAction.Create, AuditAction.Update, AuditAction.Delete);
        trail.Entries.Should().OnlyContain(e => e.ResourceType == "tax_profile" && e.ResourceId == id && e.SubjectUserId == _personA);
        trail.Entries[0].FieldNames.Should().Equal("Caveat", "Jurisdiction", "JurisdictionNotes");
        trail.Entries[1].FieldNames.Should().Equal("Caveat");
        trail.Entries[2].FieldNames.Should().BeEmpty();
        trail.Entries.SelectMany(e => e.FieldNames).Should().NotContain(n => n.Contains("Zebra"));
    }

    [DockerRequiredFact]
    public async Task AHostWithoutTheKey_ReadsTheCodeAndTheFlags_AndCannotReadOrWriteTheEncryptedColumns()
    {
        await InsertAsync(_personA, Before, caveat: null);

        await using var keyless = Context(null, _personA);
        var repository = new TaxProfileRepository(keyless);
        repository.CanReadEncryptedFields.Should().BeFalse();

        var outline = await repository.GetOutlineAsync(_personA);
        outline!.Jurisdiction.Should().Be("IE");
        (outline.HasJurisdictionNotes, outline.HasCaveat).Should().Be((true, false));

        var read = async () => await repository.GetAsync(_personA);
        await read.Should().ThrowAsync<FieldKeyUnavailableException>();

        var write = async () => await repository.AddAsync(NewProfile(_personB));
        (await write.Should().ThrowAsync<DbUpdateException>()).WithInnerException<FieldKeyUnavailableException>();
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

namespace FinanceSentry.Tests.Integration.Radar;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <c>radar_signals.Payload</c> is an open-shaped jsonb object that stays on its string conversion (no EF mapping
/// reads a <c>Dictionary&lt;string, object&gt;</c> without Npgsql's dynamic-JSON opt-in). Pins what that conversion
/// reads and writes: a stored object reads back with <see cref="JsonElement"/> values, a written payload keeps the
/// same jsonb, and a JSON <c>null</c> payload reads back as an empty dictionary. Real Postgres: the InMemory
/// provider cannot map jsonb columns.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RadarSignalPayloadJsonbTests : IAsyncLifetime
{
    private const string LegacyPayload =
        """{"driftPct":6.25,"assetClass":"Equity","breached":true,"note":null,"history":[1,2.5],"detail":{"band":{"min":50,"max":70}}}""";

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private RadarDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RadarDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private static RadarSignal NewSignal(Dictionary<string, object>? payload = null) => new()
    {
        Id = Guid.NewGuid(),
        Scanner = RadarScanners.Portfolio,
        SignalType = RadarSignalTypes.AllocationDrift,
        Severity = SignalSeverity.Notable,
        SubjectType = RadarSubjectTypes.AssetClass,
        Subject = "Equity",
        DedupKey = Guid.NewGuid().ToString("N"),
        Payload = payload ?? [],
    };

    private async Task<Guid> SeedRowAsync(string payloadJson)
    {
        await using var db = CreateContext();
        var signal = NewSignal();
        db.RadarSignals.Add(signal);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            """UPDATE radar.radar_signals SET "Payload" = {0}::jsonb WHERE "Id" = {1}""", payloadJson, signal.Id);
        return signal.Id;
    }

    private async Task<RadarSignal> ReadAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.RadarSignals.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
    }

    [DockerRequiredFact]
    public async Task Row_in_the_former_conversion_shape_reads_back_unchanged()
    {
        var id = await SeedRowAsync(LegacyPayload);

        var stored = await ReadAsync(id);

        stored.Payload.Keys.Should().BeEquivalentTo("driftPct", "assetClass", "breached", "note", "history", "detail");
        stored.Payload.Where(e => e.Key != "note").Select(e => e.Value).Should().AllSatisfy(v => v.Should().BeOfType<JsonElement>());
        ((JsonElement)stored.Payload["driftPct"]).GetDecimal().Should().Be(6.25m);
        ((JsonElement)stored.Payload["assetClass"]).GetString().Should().Be("Equity");
        ((JsonElement)stored.Payload["breached"]).GetBoolean().Should().BeTrue();
        stored.Payload["note"].Should().BeNull(); // a JSON null inside an object-typed value reads back as a CLR null
        ((JsonElement)stored.Payload["history"]).EnumerateArray().Select(e => e.GetDecimal()).Should().Equal(1m, 2.5m);
        ((JsonElement)stored.Payload["detail"]).GetProperty("band").GetProperty("max").GetInt32().Should().Be(70);
    }

    [DockerRequiredFact]
    public async Task Written_payload_keeps_the_jsonb_shape()
    {
        await using var db = CreateContext();
        var signal = NewSignal(JsonSerializer.Deserialize<Dictionary<string, object>>(LegacyPayload)!);
        db.RadarSignals.Add(signal);
        await db.SaveChangesAsync();

        var same = await db.Database.SqlQueryRaw<bool>(
            """SELECT "Payload" = {0}::jsonb AS "Value" FROM radar.radar_signals WHERE "Id" = {1}""",
            LegacyPayload, signal.Id).SingleAsync();
        same.Should().BeTrue();
    }

    [DockerRequiredFact]
    public async Task Empty_object_reads_back_as_an_empty_dictionary()
    {
        var id = await SeedRowAsync("{}");

        var stored = await ReadAsync(id);

        stored.Payload.Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Null_json_payload_reads_back_as_an_empty_dictionary()
    {
        var id = await SeedRowAsync("null");

        var stored = await ReadAsync(id);

        stored.Payload.Should().NotBeNull().And.BeEmpty();
    }
}

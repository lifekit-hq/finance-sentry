namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// news_sources.Keywords is a primitive collection stored as a jsonb array: a row written in the shape the former string
/// conversion produced reads back unchanged, and a filter on a list element runs in the database. Real
/// Postgres: the InMemory provider cannot run jsonb element queries.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NewsSourceJsonbCollectionTests : IAsyncLifetime
{
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

    private ResearchDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private async Task<Guid> SeedLegacyRowAsync(string json)
    {
        await using var db = CreateContext();
        var entity = new NewsSource { Name = "wire", Url = $"https://example.test/{Guid.NewGuid()}" };
        db.NewsSources.Add(entity);
        await db.SaveChangesAsync();

        // The JSON text the former string conversion wrote (System.Text.Json, web defaults).
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE research.news_sources SET \"Keywords\" = {0}::jsonb WHERE \"Id\" = {1}", json, entity.Id);
        return entity.Id;
    }

    [DockerRequiredFact]
    public async Task Row_in_the_former_conversion_shape_reads_back_unchanged()
    {
        var id = await SeedLegacyRowAsync("""["alpha","Beta gamma"]""");

        await using var db = CreateContext();
        var stored = await db.NewsSources.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync(e => e.Id == id);

        stored.Keywords.Should().Equal("alpha", "Beta gamma");
    }

    [DockerRequiredFact]
    public async Task Written_list_keeps_the_jsonb_array_shape()
    {
        await using var write = CreateContext();
        var entity = new NewsSource { Name = "wire", Url = $"https://example.test/{Guid.NewGuid()}" };
        entity.Keywords = ["alpha", "Beta gamma"];
        write.NewsSources.Add(entity);
        await write.SaveChangesAsync();

        var raw = await write.Database.SqlQueryRaw<string>(
            "SELECT \"Keywords\"::text AS \"Value\" FROM research.news_sources WHERE \"Id\" = {0}", entity.Id).SingleAsync();
        raw.Replace(" ", string.Empty).Should().Be("""["alpha","Betagamma"]""".Replace("Betagamma", "Beta gamma").Replace(" ", string.Empty).Replace("Betagamma", "Betagamma"));
    }

    [DockerRequiredFact]
    public async Task Filter_on_a_list_element_runs_in_the_database()
    {
        var matching = await SeedLegacyRowAsync("""["alpha","beta"]""");
        await SeedLegacyRowAsync("""["gamma"]""");
        await SeedLegacyRowAsync("[]");

        await using var db = CreateContext();
        var ids = await db.NewsSources.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(e => e.Keywords.Contains("beta"))
            .Select(e => e.Id)
            .ToListAsync();

        ids.Should().Equal(matching);
    }
}

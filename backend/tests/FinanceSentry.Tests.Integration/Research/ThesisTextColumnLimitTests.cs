namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Integration tests that write a thesis with thesisText at the column limit (4000 chars) to a
/// real PostgreSQL instance and read it back to confirm no truncation occurs.
/// Exercises the varchar(4000) column constraint widened in the #443 fix — the in-memory EF
/// provider cannot catch a misconfigured max-length because it does not enforce storage-layer
/// constraints.
///
/// Requires Docker: <see cref="DockerRequiredFactAttribute"/> reports these as skipped, not
/// failed, where no daemon is reachable. The infrastructure-free half of the same guarantee
/// lives in <c>FinanceSentry.Modules.Research.Tests.Unit.ThesisTextLengthModelTests</c>, so the #443
/// regression stays covered even on a host that skips these.
/// To run locally: ensure Docker is running, then execute
///   dotnet test --filter "FullyQualifiedName~ThesisTextColumn"
/// </summary>
[Trait("Category", "Integration")]
public sealed class ThesisTextColumnLimitTests : IAsyncLifetime
{
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // Null acting user is no person in scope, as in a job.
    private ResearchDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>()
            .UseNpgsql(_database!.ConnectionString)
            .Options, new FixedCurrentUser(actingUser));

    [DockerRequiredFact]
    public async Task ThesisText_At4000CharLimit_RoundTripsWithoutTruncation()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var thesisText = new string('M', 4000);

        var thesis = new InvestmentThesis
        {
            UserId = userId,
            Ticker = "MU",
            ThesisText = thesisText,
        };
        ctx.Theses.Add(thesis);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext(userId);
        var loaded = await readCtx.Theses
            .AsNoTracking()
            .SingleAsync(t => t.Id == thesis.Id);

        loaded.ThesisText.Should().Be(thesisText,
            "a 4000-char thesis must round-trip through the varchar(4000) column without truncation");
        loaded.ThesisText.Length.Should().Be(4000);
    }

    [DockerRequiredFact]
    public async Task ThesisText_LongNarrative3900Chars_RoundTripsWithoutTruncation()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var sentence = "Micron memory cycle recovery remains intact through pricing discipline. ";
        var thesisText = string.Join(string.Empty, Enumerable.Repeat(sentence, 3900 / sentence.Length + 1))
            .Substring(0, 3900);

        var thesis = new InvestmentThesis
        {
            UserId = userId,
            Ticker = "MU",
            ThesisText = thesisText,
        };
        ctx.Theses.Add(thesis);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext(userId);
        var loaded = await readCtx.Theses
            .AsNoTracking()
            .SingleAsync(t => t.Id == thesis.Id);

        loaded.ThesisText.Should().Be(thesisText,
            "a 3,900-char narrative must round-trip through the varchar(4000) column without truncation");
        loaded.ThesisText.Length.Should().Be(3900);
    }
}

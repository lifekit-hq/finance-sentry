namespace FinanceSentry.Modules.Research.Tests.ReferenceUniverse;

using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FinanceSentry.Modules.Research.Tests.ReferenceUniverse.ReferenceUniverseTestKit;

/// <summary>
/// The S&amp;P 500 port reads the reference-universe table and falls back to the embedded seed. The seed is
/// the breadth source for both the analyst universe and Radar's broad-universe bar ingestion, so a
/// malformed or empty list silently narrows both - every way the table can fail to answer must land on it.
/// </summary>
public sealed class Sp500ConstituentSourceTests
{
    private static (Sp500ConstituentSource Source, ServiceProvider Provider, ReferenceClock Clock) Build(string? dbName = null)
    {
        var name = dbName ?? $"sp500-source-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddSingleton<FinanceSentry.Core.Auth.ICurrentUser>(new FixedCurrentUser(null));
        services.AddDbContext<ResearchDbContext>(o => o.UseInMemoryDatabase(name));
        var provider = services.BuildServiceProvider();
        var clock = new ReferenceClock(Start);
        return (new Sp500ConstituentSource(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            NullLogger<Sp500ConstituentSource>.Instance), provider, clock);
    }

    private static void SeedTable(ServiceProvider provider, IEnumerable<string> tickers)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
        db.ReferenceListMembers.AddRange(tickers.Select(t => new ReferenceListMember
        {
            ListKey = "sp500",
            Ticker = t,
            Source = ReferenceSources.Sp500,
        }));
        db.SaveChanges();
    }

    private static string[] FakeIndex(int count) => Enumerable.Range(0, count).Select(i => $"ZZ{i:D3}").ToArray();

    [Fact]
    public void EmbeddedSeed_IsNormalisedDeduplicatedAndInYahooForm()
    {
        var seed = Sp500ConstituentSource.EmbeddedSeed;

        seed.Should().HaveCountGreaterThan(450).And.Contain("AAPL").And.Contain("BRK-B");
        seed.Should().OnlyContain(t => t == t.ToUpperInvariant() && t.Trim() == t && !t.Contains('.', StringComparison.Ordinal));
        seed.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetConstituents_FallsBackToTheEmbeddedSeedWhileTheTableIsEmpty()
    {
        var (source, provider, _) = Build();
        using var _p = provider;

        source.GetConstituents().Should().BeEquivalentTo(Sp500ConstituentSource.EmbeddedSeed);
    }

    [Fact]
    public void GetConstituents_ServesTheTableOnceItHoldsARealList()
    {
        var (source, provider, _) = Build();
        using var _p = provider;
        SeedTable(provider, FakeIndex(Sp500ConstituentSource.MinTableMembers + 5));

        var constituents = source.GetConstituents();

        constituents.Should().HaveCount(Sp500ConstituentSource.MinTableMembers + 5);
        constituents.Should().Contain("ZZ000").And.NotContain("AAPL");
    }

    [Fact]
    public void GetConstituents_IgnoresATableTooSmallToBeTheIndex()
    {
        var (source, provider, _) = Build();
        using var _p = provider;
        SeedTable(provider, FakeIndex(10));

        source.GetConstituents().Should().BeEquivalentTo(Sp500ConstituentSource.EmbeddedSeed);
    }

    [Fact]
    public void GetConstituents_FallsBackWhenTheDatabaseCannotBeRead()
    {
        var scopes = new Moq.Mock<IServiceScopeFactory>();
        scopes.Setup(s => s.CreateScope()).Throws(new InvalidOperationException("db down"));
        var source = new Sp500ConstituentSource(scopes.Object, new ReferenceClock(Start), NullLogger<Sp500ConstituentSource>.Instance);

        source.GetConstituents().Should().BeEquivalentTo(Sp500ConstituentSource.EmbeddedSeed);
    }

    [Fact]
    public void GetConstituents_CachesTheAnswerThenPicksUpTheTableAfterTheFallbackWindow()
    {
        var (source, provider, clock) = Build();
        using var _p = provider;

        var first = source.GetConstituents();
        SeedTable(provider, FakeIndex(Sp500ConstituentSource.MinTableMembers + 1));
        source.GetConstituents().Should().BeSameAs(first, "the fallback answer is cached for a moment");

        clock.Advance(Sp500ConstituentSource.FallbackCacheTtl + TimeSpan.FromSeconds(1));
        source.GetConstituents().Should().Contain("ZZ000", "the recovered table replaces the fallback");

        var fromTable = source.GetConstituents();
        clock.Advance(TimeSpan.FromMinutes(5));
        source.GetConstituents().Should().BeSameAs(fromTable, "a table answer lives for the longer window");
    }
}

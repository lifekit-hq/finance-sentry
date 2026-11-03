namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The seeded <c>detection_phrases</c> table, and the provider that reads it, behave as the constants did.
/// </summary>
public class DetectionPhraseSeedingTests
{
    private static DbContextOptions<BankSyncDbContext> Options() =>
        new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseInMemoryDatabase($"detection-phrases-{Guid.NewGuid():N}").Options;

    private static BankSyncDbContext Context(DbContextOptions<BankSyncDbContext> options) =>
        new(options, new FixedCurrentUser(null));

    [Fact]
    public void FreshSeed_WritesEverySeedRow_InSeedOrder()
    {
        using var db = Context(Options());

        CategorySeeder.Seed(db);

        db.DetectionPhrases.OrderBy(p => p.Id)
            .Select(p => new DetectionPhraseEntry(p.Kind, p.Phrase, p.Provider, p.Locale, p.MccCondition, p.CategoryCondition))
            .ToList().Should().Equal(DetectionPhraseSeedData.Entries);
    }

    [Fact]
    public void Reseed_AddsNothing_KeepsAnEdit_AndRestoresADeletedRow()
    {
        var options = Options();
        using (var db = Context(options))
        {
            CategorySeeder.Seed(db);
            var edited = db.DetectionPhrases.Single(p => p.Kind == DetectionPhraseKinds.CashLodgementMarker);
            edited.Provider = "edited";
            db.DetectionPhrases.Remove(db.DetectionPhrases.First(p => p.Phrase == "переказ"));
            db.SaveChanges();
        }

        using (var db = Context(options))
        {
            CategorySeeder.Seed(db);

            db.DetectionPhrases.Count().Should().Be(DetectionPhraseSeedData.Entries.Count);
            db.DetectionPhrases.Single(p => p.Kind == DetectionPhraseKinds.CashLodgementMarker).Provider.Should().Be("edited");
            db.DetectionPhrases.Count(p => p.Phrase == "переказ").Should().Be(1);
        }
    }

    [Fact]
    public void Provider_ReadsTheTable_AndTheSeededTableEqualsTheSeedSet()
    {
        var options = Options();
        using (var db = Context(options))
            CategorySeeder.Seed(db);

        var services = new ServiceCollection();
        services.AddScoped(_ => Context(options));
        using var sp = services.BuildServiceProvider();
        var provider = new DetectionPhraseProvider(sp.GetRequiredService<IServiceScopeFactory>());

        foreach (var kind in DetectionPhraseSeedData.Entries.Select(e => e.Kind).Distinct())
        {
            foreach (var mcc in new int?[] { null, 4829, 5411 })
                provider.Current.Phrases(kind, mcc).Should().Equal(DetectionPhraseSet.Seed.Phrases(kind, mcc), $"kind {kind}, mcc {mcc}");
        }
    }

    [Fact]
    public void Provider_FollowsTheTable_AndLoadsOnce()
    {
        var options = Options();
        using (var db = Context(options))
        {
            db.DetectionPhrases.Add(new DetectionPhrase { Kind = DetectionPhraseKinds.TransferOutPrefix, Phrase = "Sent to " });
            db.SaveChanges();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => Context(options));
        using var sp = services.BuildServiceProvider();
        var provider = new DetectionPhraseProvider(sp.GetRequiredService<IServiceScopeFactory>());

        var first = provider.Current;

        first.Phrases(DetectionPhraseKinds.TransferOutPrefix).Should().Equal("Sent to ");
        first.Phrases(DetectionPhraseKinds.CashLodgementMarker).Should().BeEmpty("only the table's rows exist");
        provider.Current.Should().BeSameAs(first);
    }
}

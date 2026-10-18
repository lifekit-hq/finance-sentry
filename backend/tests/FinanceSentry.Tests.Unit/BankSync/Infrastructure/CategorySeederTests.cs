namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The MCC map lives in code: a fresh database seeds every <c>mcc_categories</c> row from
/// <see cref="MccRangeClassifier"/>, so it agrees with a database whose rows were written earlier.
/// </summary>
public class CategorySeederTests
{
    private static DbContextOptions<BankSyncDbContext> Options() =>
        new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseInMemoryDatabase($"category-seeder-{Guid.NewGuid():N}").Options;

    private static BankSyncDbContext Context(DbContextOptions<BankSyncDbContext> options) =>
        new(options, new FixedCurrentUser(null));

    [Theory]
    [InlineData(5815, CategoryKeys.Entertainment)]      // Digital goods: books, movies, music
    [InlineData(5816, CategoryKeys.Entertainment)]      // Digital goods: games (Steam)
    [InlineData(5817, CategoryKeys.GeneralServices)]    // Digital goods: applications
    [InlineData(5818, CategoryKeys.GeneralMerchandise)] // Digital goods: large digital goods merchant
    [InlineData(5262, CategoryKeys.GeneralMerchandise)] // Marketplaces: not in the embedded dataset
    public void FreshSeed_WritesTheCodeMapping(int mcc, string expected)
    {
        using var db = Context(Options());

        CategorySeeder.Seed(db);

        db.MccCategories.Single(m => m.Mcc == mcc).CategoryKey.Should().Be(expected);
    }

    [Fact]
    public void FreshSeed_LeavesNoCodeUncategorized()
    {
        using var db = Context(Options());

        CategorySeeder.Seed(db);

        db.MccCategories.Where(m => m.CategoryKey == CategoryKeys.Uncategorized)
            .Select(m => m.Mcc).ToList().Should().BeEmpty();
    }

    [Fact]
    public void Reseed_KeepsAnExistingRow()
    {
        var options = Options();
        using (var db = Context(options))
        {
            db.MccCategories.Add(new MccCategory
            {
                Mcc = 5816, CategoryKey = CategoryKeys.GeneralServices, Description = "edited",
            });
            db.SaveChanges();
        }

        using (var db = Context(options))
        {
            CategorySeeder.Seed(db);

            db.MccCategories.Single(m => m.Mcc == 5816).CategoryKey.Should().Be(CategoryKeys.GeneralServices);
        }
    }

    [Fact]
    public void Resolver_FallsBackToTheRules_ForACodeTheTableLacks()
    {
        var options = Options();
        var services = new ServiceCollection();
        services.AddScoped(_ => Context(options));
        using var provider = services.BuildServiceProvider();

        using (var db = Context(options))
        {
            db.MccCategories.Add(new MccCategory { Mcc = 5411, CategoryKey = CategoryKeys.FoodAndDrink });
            db.SaveChanges();
        }

        var resolver = new CategoryResolver(provider.GetRequiredService<IServiceScopeFactory>());

        resolver.ResolveMcc(5411).Should().Be(CategoryKeys.FoodAndDrink);
        resolver.ResolveMcc(5816).Should().Be(CategoryKeys.Entertainment);
        resolver.ResolveMcc(null).Should().Be(CategoryKeys.Uncategorized);
    }
}

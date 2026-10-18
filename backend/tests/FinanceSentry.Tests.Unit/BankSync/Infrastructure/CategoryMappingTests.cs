namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;

public class CategoryMappingTests
{
    [Theory]
    [InlineData(5411, CategoryKeys.FoodAndDrink)]   // Grocery stores
    [InlineData(5812, CategoryKeys.FoodAndDrink)]   // Eating places
    [InlineData(4111, CategoryKeys.Transportation)] // Local transit
    [InlineData(5541, CategoryKeys.Transportation)] // Service stations (override)
    [InlineData(5311, CategoryKeys.GeneralMerchandise)] // Department stores
    [InlineData(5941, CategoryKeys.GeneralMerchandise)] // Sporting goods
    [InlineData(7911, CategoryKeys.Entertainment)]  // Dance halls
    [InlineData(5912, CategoryKeys.Medical)]        // Pharmacies (override)
    [InlineData(4812, CategoryKeys.RentAndUtilities)] // Telecom
    [InlineData(7011, CategoryKeys.Travel)]         // Lodging
    [InlineData(7230, CategoryKeys.PersonalCare)]   // Beauty shops (override)
    [InlineData(8011, CategoryKeys.Medical)]        // Doctors
    [InlineData(9399, CategoryKeys.GovernmentAndNonProfit)] // Government services
    [InlineData(4214, CategoryKeys.GeneralServices)] // Freight carriers & trucking
    [InlineData(4215, CategoryKeys.GeneralServices)] // Courier services
    [InlineData(4225, CategoryKeys.GeneralServices)] // Warehousing & storage
    [InlineData(5262, CategoryKeys.GeneralMerchandise)] // Marketplaces (override)
    [InlineData(5815, CategoryKeys.Entertainment)]  // Digital goods: media
    [InlineData(5816, CategoryKeys.Entertainment)]  // Digital goods: games
    [InlineData(5817, CategoryKeys.GeneralServices)] // Digital goods: applications
    [InlineData(5818, CategoryKeys.GeneralMerchandise)] // Digital goods: large merchant
    [InlineData(5832, CategoryKeys.GeneralMerchandise)] // Antique shops
    public void MccRangeClassifier_MapsCodesToCanonicalKeys(int mcc, string expected)
    {
        MccRangeClassifier.Classify(mcc).Should().Be(expected);
    }

    [Fact]
    public void MccRangeClassifier_UnknownCode_IsUncategorized()
    {
        MccRangeClassifier.Classify(1).Should().Be(CategoryKeys.Uncategorized);
    }

    [Theory]
    [InlineData("Groceries", CategoryKeys.FoodAndDrink)]
    [InlineData("Restaurants", CategoryKeys.FoodAndDrink)]
    [InlineData("Shopping", CategoryKeys.GeneralMerchandise)]
    [InlineData("Healthcare", CategoryKeys.Medical)]
    [InlineData("Bills & Utilities", CategoryKeys.RentAndUtilities)]
    [InlineData("Auto & Transport", CategoryKeys.Transportation)]
    [InlineData("Travel", CategoryKeys.Travel)]
    [InlineData("Home Improvement", CategoryKeys.HomeImprovement)]
    [InlineData("Entertainment", CategoryKeys.Entertainment)]
    public void TrueLayerCategoryMapper_MapsCommonClassificationLabels(string rawCategory, string expected)
    {
        new TrueLayerCategoryMapper().Map([rawCategory]).Should().Be(expected);
    }

    [Fact]
    public void TrueLayerCategoryMapper_UnknownLabel_IsUncategorized()
    {
        new TrueLayerCategoryMapper().Map(["Something Unknown"]).Should().Be(CategoryKeys.Uncategorized);
    }
}

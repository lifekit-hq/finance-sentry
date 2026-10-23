namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;

/// <summary>
/// Exercises the real curated merchant-keyword seed list through the same matcher the
/// resolver uses, over actual transaction descriptions observed from TrueLayer. Guards both
/// the seed data quality and the longest-first matching rule without needing a database.
/// </summary>
public class MerchantKeywordSeedDataTests
{
    private static readonly IReadOnlyList<(string Keyword, string CategoryKey)> Prepared =
        MerchantKeywordMatcher.Prepare(MerchantKeywordSeedData.Keywords);

    [Theory]
    [InlineData("Lidl Ireland Ltd", CategoryKeys.FoodAndDrink)]
    [InlineData("Tesco Stores 6913", CategoryKeys.FoodAndDrink)]
    [InlineData("Camile Thai Dublin", CategoryKeys.FoodAndDrink)]
    [InlineData("amazon.ie", CategoryKeys.GeneralMerchandise)]
    [InlineData("Openai *chatgpt Subscr", CategoryKeys.GeneralServices)]
    [InlineData("Github, Inc.", CategoryKeys.GeneralServices)]
    [InlineData("Netcup", CategoryKeys.GeneralServices)]
    [InlineData("Leap Card App", CategoryKeys.Transportation)]
    [InlineData("Fitzmaurice Chemists", CategoryKeys.Medical)]
    [InlineData("East Wall Medical Cent", CategoryKeys.Medical)]
    [InlineData("apple.com/bill", CategoryKeys.GeneralServices)]
    [InlineData("Hetzner Online Gmbh", CategoryKeys.GeneralServices)]
    [InlineData("Feel Fit Gym", CategoryKeys.Medical)]
    [InlineData("Dublinbikes Internet", CategoryKeys.Transportation)]
    [InlineData("Sq *olives Room Cafe 1", CategoryKeys.FoodAndDrink)]
    [InlineData("*MOBI TOP-UP 0857860057", CategoryKeys.RentAndUtilities)]
    [InlineData("Klarna*mstore.ie", CategoryKeys.GeneralMerchandise)]
    // Monobank installment / fee wordings (#581) — real spending behind the wire MCC 4829
    [InlineData("Погашення наступного платежу ТОВ Алло - monomarket", CategoryKeys.LoanPayments)]
    [InlineData("Повне погашення Pandora", CategoryKeys.LoanPayments)]
    [InlineData("Платіж RozetkaPay щомісячний платіж", CategoryKeys.LoanPayments)]
    [InlineData("Обслуговування платинової картки за серпень", CategoryKeys.BankFees)]
    // TrueLayer no-MCC merchants (observed uncategorized in prod)
    [InlineData("Popeyes", CategoryKeys.FoodAndDrink)]
    [InlineData("Cineworld", CategoryKeys.Entertainment)]
    [InlineData("Frn* Hold.free-Now.com", CategoryKeys.Transportation)]
    [InlineData("veoliatransport.com", CategoryKeys.Transportation)]
    [InlineData("Dt Dublin Express", CategoryKeys.Transportation)]
    [InlineData("Lego Blanchardstown", CategoryKeys.GeneralMerchandise)]
    [InlineData("Makeup", CategoryKeys.PersonalCare)]
    [InlineData("Clubwise Software Ltd", CategoryKeys.Medical)]
    // Travel agencies: "esky eskyie" contains "sky " and used to land in RENT_AND_UTILITIES (#1235)
    [InlineData("ESKY ESKYIE", CategoryKeys.Travel)]
    [InlineData("Skyscanner Ltd", CategoryKeys.Travel)]
    [InlineData("Kiwi.com Brno", CategoryKeys.Travel)]
    [InlineData("Opodo Ltd", CategoryKeys.Travel)]
    [InlineData("Sky Digital 1234", CategoryKeys.RentAndUtilities)]
    [InlineData("DD Sky Ireland", CategoryKeys.RentAndUtilities)]
    public void Resolve_KnownMerchantDescriptions_MapToExpectedCategory(string description, string expected)
    {
        MerchantKeywordMatcher.Resolve(description, Prepared).Should().Be(expected);
    }

    [Fact]
    public void Resolve_TrailingSpaceKeyword_DoesNotMatchMidWord()
    {
        MerchantKeywordMatcher.Resolve("Esky Eskyie", MerchantKeywordMatcher.Prepare([("sky ", CategoryKeys.RentAndUtilities)]))
            .Should().Be(CategoryKeys.Uncategorized);
    }

    [Fact]
    public void Resolve_LongerKeywordWins_UberEatsIsFoodNotTransport()
    {
        MerchantKeywordMatcher.Resolve("UBER EATS Amsterdam", Prepared).Should().Be(CategoryKeys.FoodAndDrink);
        MerchantKeywordMatcher.Resolve("Uber trip Dublin", Prepared).Should().Be(CategoryKeys.Transportation);
    }

    [Fact]
    public void Resolve_LongerKeywordWins_GymbeamIsMerchandiseNotGym()
    {
        MerchantKeywordMatcher.Resolve("Gymbeam Italy S.r.l.", Prepared).Should().Be(CategoryKeys.GeneralMerchandise);
        MerchantKeywordMatcher.Resolve("Feel Fit Gym", Prepared).Should().Be(CategoryKeys.Medical);
    }

    [Fact]
    public void Resolve_LongerKeywordWins_TicketmasterViaKlarnaIsEntertainment()
    {
        MerchantKeywordMatcher.Resolve("Klarna* Ticketmaster.", Prepared).Should().Be(CategoryKeys.Entertainment);
    }

    [Theory]
    [InlineData("To Denys Sychov")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_UnknownOrEmpty_IsUncategorized(string? description)
    {
        MerchantKeywordMatcher.Resolve(description, Prepared).Should().Be(CategoryKeys.Uncategorized);
    }

    [Fact]
    public void SeedData_AllCategoryKeysAreCanonical()
    {
        var canonical = new HashSet<string>
        {
            CategoryKeys.Income, CategoryKeys.TransferIn, CategoryKeys.TransferOut,
            CategoryKeys.LoanPayments, CategoryKeys.BankFees, CategoryKeys.Entertainment,
            CategoryKeys.FoodAndDrink, CategoryKeys.GeneralMerchandise, CategoryKeys.HomeImprovement,
            CategoryKeys.Medical, CategoryKeys.PersonalCare, CategoryKeys.GeneralServices,
            CategoryKeys.GovernmentAndNonProfit, CategoryKeys.Transportation, CategoryKeys.Travel,
            CategoryKeys.RentAndUtilities, CategoryKeys.Uncategorized,
        };

        MerchantKeywordSeedData.Keywords.Select(k => k.CategoryKey)
            .Should().OnlyContain(key => canonical.Contains(key));
    }

    [Fact]
    public void SeedData_HasNoDuplicateKeywords()
    {
        MerchantKeywordSeedData.Keywords.Select(k => k.Keyword.ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();
    }
}

namespace FinanceSentry.Tests.Unit.Subscriptions.Icons;

using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;
using FluentAssertions;
using Xunit;

public class MerchantBrandMatcherTests
{
    private static readonly IReadOnlyList<MerchantBrand> Brands =
    [
        new() { Id = 1, Keyword = "netflix", Domain = "www.netflix.com" },
        new() { Id = 2, Keyword = "wizz air", Domain = "www.wizzair.com" },
        new() { Id = 3, Keyword = "apple", Domain = "www.apple.com" },
        new() { Id = 4, Keyword = "apple music", Domain = "music.apple.com" },
    ];

    [Theory]
    [InlineData("NETFLIX.COM", 1)]
    [InlineData("Netflix", 1)]
    [InlineData("*NETFLIX 866-579", 1)]
    [InlineData("WIZZ AIR 4567", 2)]
    [InlineData("Wizz-Air", 2)]
    public void Match_NameContainsKeywordTokens_ReturnsThatBrand(string name, int expectedId) =>
        MerchantBrandMatcher.Match(Brands, name)!.Id.Should().Be(expectedId);

    [Theory]
    [InlineData("Netflixx")]
    [InlineData("pineapple store")]
    [InlineData("wizz")]
    [InlineData("air wizz")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Match_NoWholeKeywordInName_ReturnsNull(string? name) =>
        MerchantBrandMatcher.Match(Brands, name).Should().BeNull();

    [Fact]
    public void Match_SeveralKeywordsMatch_MostSpecificWins() =>
        MerchantBrandMatcher.Match(Brands, "Apple Music subscription")!.Id.Should().Be(4);

    [Fact]
    public void Match_NoBrands_ReturnsNull() =>
        MerchantBrandMatcher.Match([], "Netflix").Should().BeNull();

    [Fact]
    public void Seed_EveryKeywordMatchesItself_AndEveryHostIsAPlainHostName()
    {
        var brands = MerchantBrandSeedData.Entries
            .Select((e, i) => new MerchantBrand { Id = i + 1, Keyword = e.Keyword, Domain = e.Domain })
            .ToList();

        foreach (var brand in brands)
            MerchantBrandMatcher.Match(brands, brand.Keyword)!.Keyword.Should().Be(brand.Keyword);

        brands.Select(b => b.Keyword).Should().OnlyHaveUniqueItems();
        brands.Select(b => b.Domain).Should().OnlyContain(d => PublicAddressGuard.IsPlainHostName(d));
    }
}

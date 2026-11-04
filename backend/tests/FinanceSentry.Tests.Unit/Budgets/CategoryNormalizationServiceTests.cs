namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Modules.Budgets.Application.Services;
using FluentAssertions;
using Xunit;

public class CategoryNormalizationServiceTests
{
    private readonly CategoryNormalizationService _sut = new();

    [Theory]
    [InlineData("food_and_drink", "FOOD_AND_DRINK")]
    [InlineData("housing", "HOME_IMPROVEMENT")]
    [InlineData("transport", "TRANSPORTATION")]
    [InlineData("shopping", "GENERAL_MERCHANDISE")]
    [InlineData("entertainment", "ENTERTAINMENT")]
    [InlineData("health", "MEDICAL")]
    [InlineData("utilities", "RENT_AND_UTILITIES")]
    [InlineData("travel", "TRAVEL")]
    [InlineData("other", "UNCATEGORIZED")]
    public void Normalize_LegacyKey_RemapsToAdoptedKey(string legacy, string expectedKey)
    {
        _sut.Normalize(legacy).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData("FOOD_AND_DRINK")]
    [InlineData("GENERAL_MERCHANDISE")]
    [InlineData("TRANSPORTATION")]
    [InlineData("MEDICAL")]
    [InlineData("UNCATEGORIZED")]
    public void Normalize_AdoptedKey_ReturnsSameKey(string key)
    {
        _sut.Normalize(key).Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unrecognised_category_xyz")]
    [InlineData("PayPal")]
    public void Normalize_NullOrUnknown_ReturnsUncategorized(string? raw)
    {
        _sut.Normalize(raw).Should().Be("UNCATEGORIZED");
    }

    [Theory]
    [InlineData("FOOD_AND_DRINK", "Food & Drink")]
    [InlineData("GENERAL_MERCHANDISE", "Shopping")]
    [InlineData("TRANSPORTATION", "Transport")]
    [InlineData("MEDICAL", "Health")]
    [InlineData("RENT_AND_UTILITIES", "Bills & Utilities")]
    [InlineData("UNCATEGORIZED", "Uncategorized")]
    public void GetLabel_AdoptedKey_ReturnsDisplayName(string key, string expectedLabel)
    {
        _sut.GetLabel(key).Should().Be(expectedLabel);
    }

    [Fact]
    public void GetLabel_LegacyKey_ReturnsAdoptedDisplayName()
    {
        _sut.GetLabel("food_and_drink").Should().Be("Food & Drink");
    }

    [Fact]
    public void GetLabel_UnknownKey_ReturnsUncategorizedFallback()
    {
        _sut.GetLabel("nonexistent").Should().Be("Uncategorized");
    }

    [Fact]
    public void Household_IsAKnownLine_WithItsOwnLabel()
    {
        var sut = new CategoryNormalizationService();

        Assert.Equal("HOUSEHOLD", sut.Normalize("household"));
        Assert.Equal("Household bills", sut.GetLabel("HOUSEHOLD"));
    }
}

namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FluentAssertions;
using Xunit;

/// <summary>
/// Pins the stored-<c>SourceCategory</c> round trip. Ingest categorizes from the live
/// classification while the recategorization backfill only has the stored string, so if the two
/// forms drift the backfill silently skips the provider rung of the categorization ladder and a
/// lower rung re-decides rows ingest had already classified correctly (#553).
/// </summary>
public class TrueLayerCategoryMapperTests
{
    private readonly TrueLayerCategoryMapper _sut = new();

    [Theory]
    [InlineData("Restaurants")]
    [InlineData("Groceries")]
    [InlineData("Mortgage")]
    public void MapStored_AgreesWithMap_ForASingleSegmentClassification(string classification)
    {
        _sut.MapStored(TrueLayerCategoryMapper.ToSourceCategory([classification]))
            .Should().Be(_sut.Map([classification]));
    }

    [Fact]
    public void MapStored_AgreesWithMap_ForAMultiSegmentClassificationPath()
    {
        string[] classification = ["Groceries", "Supermarket"];

        _sut.MapStored(TrueLayerCategoryMapper.ToSourceCategory(classification))
            .Should().Be(_sut.Map(classification))
            .And.Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public void MapStored_FallsBackToTheSecondSegment_WhenTheFirstIsUnknown()
    {
        // The join/split must preserve every segment: Map scans the whole path for the first
        // segment it knows, so losing the tail would silently downgrade the row.
        string[] classification = ["Nonsense Bank Label", "Restaurants"];

        _sut.MapStored(TrueLayerCategoryMapper.ToSourceCategory(classification))
            .Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nothing The Lookup Knows")]
    public void MapStored_ReturnsUncategorized_WhenNothingMaps(string? sourceCategory)
    {
        // UNCATEGORIZED is the ladder's "not claimed" signal, so an unmappable wording lets the
        // rungs below the provider rung decide rather than parking the row.
        _sut.MapStored(sourceCategory).Should().Be(CategoryKeys.Uncategorized);
    }

    [Fact]
    public void ToSourceCategory_IsNull_WhenTheProviderClassifiedNothing()
    {
        // Null keeps the row in the backfill's pass-2 re-fetch set; an empty string would not.
        TrueLayerCategoryMapper.ToSourceCategory(null).Should().BeNull();
        TrueLayerCategoryMapper.ToSourceCategory([]).Should().BeNull();
    }
}

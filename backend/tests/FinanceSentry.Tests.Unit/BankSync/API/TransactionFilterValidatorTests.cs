namespace FinanceSentry.Tests.Unit.BankSync.API;

using FinanceSentry.Modules.BankSync.API.Validation;
using FluentAssertions;
using Xunit;

public sealed class TransactionFilterValidatorTests
{
    [Fact]
    public void ValidateDate_NullOrWhitespace_ReturnsNoErrorAndNullParsed()
    {
        var error = TransactionFilterValidator.ValidateDate(null, out var parsed);

        error.Should().BeNull();
        parsed.Should().BeNull();
    }

    [Fact]
    public void ValidateDate_ValidFormat_ReturnsNoErrorAndParsedDate()
    {
        var error = TransactionFilterValidator.ValidateDate("2026-03-15", out var parsed);

        error.Should().BeNull();
        parsed.Should().Be(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void ValidateDate_InvalidFormat_ReturnsInvalidDateRangeError()
    {
        var error = TransactionFilterValidator.ValidateDate("15-03-2026", out var parsed);

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_DATE_RANGE");
        parsed.Should().BeNull();
    }

    [Fact]
    public void ValidateDateRange_FromAfterTo_ReturnsInvalidDateRangeError()
    {
        var error = TransactionFilterValidator.ValidateDateRange(
            new DateTime(2026, 6, 1), new DateTime(2026, 1, 1));

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_DATE_RANGE");
    }

    [Fact]
    public void ValidateDateRange_FromBeforeOrEqualTo_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateDateRange(
            new DateTime(2026, 1, 1), new DateTime(2026, 6, 1));

        error.Should().BeNull();
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(null, -1.0)]
    public void ValidateAmountRange_NegativeBound_ReturnsInvalidAmountRangeError(double? min, double? max)
    {
        var minDecimal = (decimal?)min;
        var maxDecimal = (decimal?)max;
        var error = TransactionFilterValidator.ValidateAmountRange(minDecimal, maxDecimal);

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_AMOUNT_RANGE");
    }

    [Fact]
    public void ValidateAmountRange_MinGreaterThanMax_ReturnsInvalidAmountRangeError()
    {
        var error = TransactionFilterValidator.ValidateAmountRange(100m, 10m);

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_AMOUNT_RANGE");
    }

    [Fact]
    public void ValidateAmountRange_ValidBounds_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateAmountRange(10m, 100m);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateCategories_Null_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateCategories(null);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateCategories_KnownCategory_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateCategories(["FOOD_AND_DRINK"]);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateCategories_UnknownCategory_ReturnsInvalidCategoryError()
    {
        var error = TransactionFilterValidator.ValidateCategories(["NOT_A_REAL_CATEGORY"]);

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_CATEGORY");
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("credit")]
    [InlineData("DEBIT")]
    public void ValidateTransactionType_AllowedValue_ReturnsNull(string value)
    {
        var error = TransactionFilterValidator.ValidateTransactionType(value);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateTransactionType_Null_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateTransactionType(null);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateTransactionType_Unknown_ReturnsInvalidTransactionTypeError()
    {
        var error = TransactionFilterValidator.ValidateTransactionType("transfer");

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_TRANSACTION_TYPE");
    }

    [Fact]
    public void ValidateSearch_Null_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateSearch(null);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateSearch_WithinLimit_ReturnsNull()
    {
        var error = TransactionFilterValidator.ValidateSearch(new string('a', TransactionFilterValidator.MaxSearchLength));

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateSearch_TooLong_ReturnsInvalidSearchError()
    {
        var error = TransactionFilterValidator.ValidateSearch(new string('a', TransactionFilterValidator.MaxSearchLength + 1));

        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("INVALID_SEARCH");
    }
}

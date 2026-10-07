using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Validators;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.Auth;

public class UpdateProfileCommandValidatorTests
{
    private readonly UpdateProfileCommandValidator _validator = new();

    [Theory]
    [InlineData("0.005", true)]
    [InlineData("0.04", true)]
    [InlineData("0.10", true)]
    [InlineData("0.0049", false)]
    [InlineData("0.1001", false)]
    [InlineData("0", false)]
    [InlineData("-0.04", false)]
    public void SafeWithdrawalRate_IsBoundedToHalfPercentThroughTenPercent(string rate, bool valid)
    {
        var result = _validator.Validate(Command(decimal.Parse(rate), 0.05m));

        result.IsValid.Should().Be(valid);
        if (!valid)
            result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(UpdateProfileCommand.SafeWithdrawalRate));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("0.05", true)]
    [InlineData("0.15", true)]
    [InlineData("-0.0001", false)]
    [InlineData("0.1501", false)]
    public void RealAnnualReturn_IsBoundedToZeroThroughFifteenPercent(string rate, bool valid)
    {
        var result = _validator.Validate(Command(0.04m, decimal.Parse(rate)));

        result.IsValid.Should().Be(valid);
        if (!valid)
            result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(UpdateProfileCommand.RealAnnualReturn));
    }

    private static UpdateProfileCommand Command(decimal withdrawalRate, decimal realReturn) =>
        new(Guid.NewGuid(), "Fire", "Bounds", "USD", "dark", true, false, 100m, true, false, withdrawalRate, realReturn);
}

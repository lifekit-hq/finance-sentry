using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Validators;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.Auth;

public class UpdatePreferencesCommandValidatorTests
{
    private readonly UpdatePreferencesCommandValidator _validator = new();

    [Theory]
    [InlineData("system", true)]
    [InlineData("light", true)]
    [InlineData("dark", true)]
    [InlineData("", false)]
    [InlineData("Dark", false)]
    [InlineData("sepia", false)]
    public void Theme_MustBeAKnownMode(string theme, bool valid) =>
        _validator.Validate(Command(new AppearanceDto(theme, null, null))).IsValid.Should().Be(valid);

    [Theory]
    [InlineData("#175a6d", true)]
    [InlineData("#AABBCC", true)]
    [InlineData("175a6d", false)]
    [InlineData("#175a6", false)]
    [InlineData("#175a6dff", false)]
    [InlineData("#gggggg", false)]
    [InlineData("", false)]
    public void Seed_MustBeAHexColour(string seed, bool valid) =>
        _validator.Validate(Command(new AppearanceDto("system", seed, null))).IsValid.Should().Be(valid);

    [Theory]
    [InlineData(0d, true)]
    [InlineData(0.5d, true)]
    [InlineData(1d, true)]
    [InlineData(-0.01d, false)]
    [InlineData(1.01d, false)]
    public void Intensity_IsBoundedToZeroThroughOne(double intensity, bool valid) =>
        _validator.Validate(Command(new AppearanceDto("system", null, intensity))).IsValid.Should().Be(valid);

    [Fact]
    public void NoAppearance_IsValid_BecauseAnOmittedFieldIsUntouched() =>
        _validator.Validate(Command(null)).IsValid.Should().BeTrue();

    private static UpdatePreferencesCommand Command(AppearanceDto? appearance) => new(Guid.NewGuid(), appearance);
}

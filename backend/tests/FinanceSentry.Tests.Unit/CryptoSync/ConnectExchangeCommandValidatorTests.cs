using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Application.Validators;
using FinanceSentry.Modules.CryptoSync.Domain;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class ConnectExchangeCommandValidatorTests
{
    private readonly ConnectExchangeCommandValidator _sut = new();

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public void ValidCommand_Passes(string provider)
    {
        _sut.Validate(new ConnectExchangeCommand(Guid.NewGuid(), provider, "key", "secret")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance, "apiSecret is required.")]
    [InlineData(CryptoExchangeProvider.RevolutX, "privateKey is required.")]
    public void MissingSecret_NamesTheFieldTheVenueAskedFor(string provider, string message)
    {
        var result = _sut.Validate(new ConnectExchangeCommand(Guid.NewGuid(), provider, "key", ""));

        result.Errors.Select(e => e.ErrorMessage).Should().Equal(message);
    }

    [Fact]
    public void MissingApiKey_Fails()
    {
        _sut.Validate(new ConnectExchangeCommand(Guid.NewGuid(), CryptoExchangeProvider.RevolutX, "", "pem"))
            .Errors.Select(e => e.ErrorMessage).Should().Equal("apiKey is required.");
    }
}

namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class SetAssetPriceTargetCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAssetPriceTargetRepository> _targets = new();

    [Fact]
    public async Task Set_NormalisesAndSaves()
    {
        AssetPriceTarget? saved = null;
        _targets.Setup(t => t.UpsertAsync(It.IsAny<AssetPriceTarget>(), It.IsAny<CancellationToken>()))
            .Callback<AssetPriceTarget, CancellationToken>((t, _) => saved = t);

        var result = await Handler().Handle(
            new SetAssetPriceTargetCommand(Guid.NewGuid(), " aapl ", 250.123456m, " eur "), CancellationToken.None);

        saved!.Symbol.Should().Be("AAPL");
        saved.TargetPrice.Should().Be(250.1235m);
        saved.Currency.Should().Be("EUR");
        saved.UpdatedAt.Should().Be(Now);
        result.Should().Be(new DossierUserTarget(250.1235m, "EUR", Now));
    }

    [Fact]
    public async Task Set_SubCentPrice_KeepsItsSignificantDigits()
    {
        var result = await Handler().Handle(
            new SetAssetPriceTargetCommand(Guid.NewGuid(), "SHIB-USD", 0.00002345m, "USD"), CancellationToken.None);

        result.TargetPrice.Should().Be(0.00002345m);
    }

    [Fact]
    public async Task Set_BlankCurrency_IsStoredAsNone()
    {
        var result = await Handler().Handle(
            new SetAssetPriceTargetCommand(Guid.NewGuid(), "MU", 100m, " "), CancellationToken.None);

        result.Currency.Should().BeNull();
    }

    [Theory]
    [InlineData("MU", 0, null)]
    [InlineData("MU", -1, null)]
    [InlineData("MU", 1_000_000_001, null)]
    [InlineData("MU", 10, "US")]
    [InlineData("MU", 10, "U5D")]
    [InlineData(" ", 10, null)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", 10, null)]
    public async Task Set_InvalidInput_ThrowsAndSavesNothing(string symbol, double price, string? currency)
    {
        var act = () => Handler().Handle(
            new SetAssetPriceTargetCommand(Guid.NewGuid(), symbol, (decimal)price, currency), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidPriceTargetException>()).Which.ErrorCode.Should().Be("INVALID_PRICE_TARGET");
        _targets.Verify(t => t.UpsertAsync(It.IsAny<AssetPriceTarget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Clear_DelegatesToTheRepository()
    {
        var user = Guid.NewGuid();
        _targets.Setup(t => t.DeleteAsync(user, "MU", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var removed = await new ClearAssetPriceTargetCommandHandler(_targets.Object)
            .Handle(new ClearAssetPriceTargetCommand(user, "MU"), CancellationToken.None);

        removed.Should().BeTrue();
    }

    private SetAssetPriceTargetCommandHandler Handler() => new(_targets.Object, new ManualTimeProvider(Now));
}

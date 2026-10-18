namespace FinanceSentry.Tests.Unit.CryptoSync;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Application.Queries;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetCryptoHoldingsVisibilityTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly UserAssetVisibility HideSmall = new(true, true, 5m);

    private static async Task<CryptoHoldingsResponse> QueryAsync(UserAssetVisibility? prefs, params CryptoHolding[] rows)
    {
        var holdings = new Mock<ICryptoHoldingRepository>();
        holdings.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        var visibility = new Mock<IUserAssetVisibilityReader>();
        visibility.Setup(v => v.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(prefs);
        var currency = new Mock<IUserBaseCurrencyReader>();
        currency.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync("USD");

        return await new GetCryptoHoldingsQueryHandler(holdings.Object, visibility.Object, currency.Object)
            .Handle(new GetCryptoHoldingsQuery(UserId), CancellationToken.None);
    }

    [Fact]
    public async Task Dust_is_flagged_small_and_still_counts_in_the_total()
    {
        var response = await QueryAsync(HideSmall,
            CryptoHolding.Create(UserId, CryptoExchangeProvider.Binance, "BTC", 0.5m, 0m, 30000m),
            CryptoHolding.Create(UserId, CryptoExchangeProvider.Binance, "SHIB", 1000m, 0m, 0.02m));

        response.Holdings.Select(h => (h.Asset, h.HiddenReason)).Should().BeEquivalentTo(
            [("BTC", (string?)null), ("SHIB", "small")]);
        response.TotalUsdValue.Should().Be(30000.02m);
        response.Holdings.Sum(h => h.UsdValue).Should().Be(response.TotalUsdValue);
    }

    [Fact]
    public async Task An_unpriced_holding_is_never_hidden()
    {
        var response = await QueryAsync(HideSmall,
            CryptoHolding.Create(UserId, CryptoExchangeProvider.Binance, "NEWCOIN", 10m, 0m, 0m));

        response.Holdings.Single().HiddenReason.Should().BeNull();
    }

    [Fact]
    public async Task Nothing_is_flagged_while_small_hiding_is_off()
    {
        var response = await QueryAsync(new UserAssetVisibility(true, false, 5m),
            CryptoHolding.Create(UserId, CryptoExchangeProvider.Binance, "SHIB", 1000m, 0m, 0.02m));

        response.Holdings.Single().HiddenReason.Should().BeNull();
    }
}

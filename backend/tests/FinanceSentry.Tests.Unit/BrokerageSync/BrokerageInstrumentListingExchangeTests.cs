namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>The listing exchange a Flex statement reports lives on the durable instrument row and is read back through the holdings reader.</summary>
public class BrokerageInstrumentListingExchangeTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void New_StoresATrimmedExchange_AndABlankOneAsNull()
    {
        new BrokerageInstrument(User, "ibkr", 1, "AAA", "STK", listingExchange: " NASDAQ ").ListingExchange.Should().Be("NASDAQ");
        new BrokerageInstrument(User, "ibkr", 2, "BBB", "STK", listingExchange: "  ").ListingExchange.Should().BeNull();
        new BrokerageInstrument(User, "ibkr", 3, "CCC", "STK").ListingExchange.Should().BeNull();
    }

    [Fact]
    public void RefreshIdentity_FillsAnUnknownExchange_ReplacesAChangedOne_AndNeverBlanksIt()
    {
        var instrument = new BrokerageInstrument(User, "ibkr", 1, "AAA", "STK");

        instrument.RefreshIdentity("AAA", "STK", null, "NASDAQ");
        instrument.ListingExchange.Should().Be("NASDAQ");

        instrument.RefreshIdentity("AAA", "STK", null);
        instrument.ListingExchange.Should().Be("NASDAQ", "the OAuth sync reports no exchange");

        instrument.RefreshIdentity("AAA", "STK", null, " ");
        instrument.ListingExchange.Should().Be("NASDAQ", "a blank value is not a report");

        instrument.RefreshIdentity("AAA", "STK", null, "NYSE");
        instrument.ListingExchange.Should().Be("NYSE", "the feed is the authority when it does report one");
    }

    [Fact]
    public async Task HoldingsReader_ReportsTheExchangeOfTheHoldingsInstrument_AndNullForCashOrAnUnreportedOne()
    {
        var withExchange = new BrokerageInstrument(User, "ibkr", 1, "AAA", "STK", listingExchange: "NASDAQ");
        var withoutExchange = new BrokerageInstrument(User, "ibkr", 2, "BBB", "STK");
        var holdings = new List<BrokerageHolding>
        {
            new(User, "AAA", "STK", 1m, 100m, "ibkr", instrumentId: withExchange.Id),
            new(User, "BBB", "STK", 1m, 100m, "ibkr", instrumentId: withoutExchange.Id),
            new(User, "USD Cash", "CASH", 50m, 50m, "ibkr"),
        };

        var holdingRepo = new Mock<IBrokerageHoldingRepository>();
        holdingRepo.Setup(r => r.GetByUserIdUnscopedAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(holdings);
        var tradeRepo = new Mock<IBrokerageTradeRepository>();
        tradeRepo.Setup(r => r.GetByUserIdUnscopedAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var instrumentRepo = new Mock<IBrokerageInstrumentRepository>();
        instrumentRepo.Setup(r => r.GetByUserIdUnscopedAsync(User, It.IsAny<CancellationToken>()))
            .ReturnsAsync([withExchange, withoutExchange]);
        var reader = new BrokerageHoldingsReader(holdingRepo.Object, tradeRepo.Object, instrumentRepo.Object, new BrokerageCostBasisReconciler());

        var rows = (await reader.GetHoldingsAsync(User)).ToDictionary(r => r.Symbol);

        rows["AAA"].ListingExchange.Should().Be("NASDAQ");
        rows["BBB"].ListingExchange.Should().BeNull();
        rows["USD Cash"].ListingExchange.Should().BeNull();
    }
}

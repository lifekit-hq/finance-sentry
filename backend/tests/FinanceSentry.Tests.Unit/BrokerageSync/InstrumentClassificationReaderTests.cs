namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class InstrumentClassificationReaderTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static BrokerageInstrument Instrument(long conid, InstrumentClassification? classification)
    {
        var instrument = new BrokerageInstrument(User, "ibkr", conid, "VUAA", "ETF");
        instrument.SetClassification(classification);
        return instrument;
    }

    private static InstrumentClassificationReader Reader(params BrokerageInstrument[] instruments)
    {
        var repository = new Mock<IBrokerageInstrumentRepository>();
        repository
            .Setup(r => r.GetBySymbolUnscopedAsync(User, "VUAA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instruments);
        return new InstrumentClassificationReader(repository.Object);
    }

    [Fact]
    public async Task GetForSymbol_ReturnsTheSetClassification()
    {
        var result = await Reader(Instrument(1, InstrumentClassification.OffshoreFund)).GetForSymbolAsync(User, "VUAA");

        result.Should().Be(InstrumentTaxClass.OffshoreFund);
    }

    [Fact]
    public async Task GetForSymbol_ReturnsNull_WhenThereIsNoInstrument()
    {
        (await Reader().GetForSymbolAsync(User, "VUAA")).Should().BeNull();
    }

    [Fact]
    public async Task GetForSymbol_ReturnsNull_WhenTheInstrumentIsUnclassified()
    {
        (await Reader(Instrument(1, null)).GetForSymbolAsync(User, "VUAA")).Should().BeNull();
    }

    [Fact]
    public async Task GetForSymbol_ReturnsNull_WhenInstrumentsInTheSymbolDisagree()
    {
        var result = await Reader(
            Instrument(1, InstrumentClassification.OffshoreFund),
            Instrument(2, InstrumentClassification.OrdinaryShare)).GetForSymbolAsync(User, "VUAA");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetForSymbol_ReturnsNull_WhenOneOfTwoInstrumentsIsUnclassified()
    {
        var result = await Reader(
            Instrument(1, InstrumentClassification.OffshoreFund),
            Instrument(2, null)).GetForSymbolAsync(User, "VUAA");

        result.Should().BeNull();
    }
}

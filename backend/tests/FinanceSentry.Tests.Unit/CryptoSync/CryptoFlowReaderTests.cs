using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class CryptoFlowReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Linked = new(2027, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Apr9 = new(2027, 4, 9, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IHistoricalExchangeRateService> _fx = new();

    public CryptoFlowReaderTests()
    {
        // EURUSD 1.10, as fixture 19 sets it.
        _fx.Setup(f => f.GetPublishedSeriesAsync("EUR", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DateOnly, decimal> { [DateOnly.FromDateTime(Apr9)] = 1.10m });
    }

    private static CryptoSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<CryptoSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static ExchangeCredential Link(string provider, bool walked = true, Guid? userId = null)
    {
        var credential = ExchangeCredential.Create(userId ?? UserId, provider, [1], [1], [1], [1], [1], [1], 1);
        typeof(ExchangeCredential).GetProperty(nameof(ExchangeCredential.CreatedAt))!.SetValue(credential, Linked);
        if (walked)
        {
            credential.MarkFlowsWalked(Apr9);
        }

        return credential;
    }

    private static CryptoFlowEvent Event(
        string provider, string type, DateTime at, decimal amount, string unit, string? counterparty,
        decimal? fee = null, decimal? priceUsd = null, DateTime? recordedAt = null, Guid? userId = null, bool? isFiat = null) =>
        CryptoFlowEvent.Create(userId ?? UserId, provider,
            new CryptoFlow(Guid.NewGuid().ToString(), type, at, amount, unit, isFiat ?? priceUsd is null, priceUsd, fee, fee is null ? null : unit,
                counterparty, "completed"),
            recordedAt ?? at);

    private CryptoFlowReader Sut(CryptoSyncDbContext db) => new(db, _fx.Object);

    [Fact]
    public async Task WithNoLinkedVenue_IsEmpty()
    {
        await using var db = NewDb();
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9, 500m, "EUR", "revolut"));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Should().BeSameAs(SleeveFlowReading.Empty);
    }

    [Fact]
    public async Task Fixture19_ConvertsEachFiatLegAtItsDatesRate_AndLeavesHistoryUpToTheLinkDayOut()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.AddRange(Link(CryptoExchangeProvider.RevolutX), Link(CryptoExchangeProvider.Binance));
        db.CryptoFlowEvents.AddRange(
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9, 500m, "EUR", "revolut"),
            Event(CryptoExchangeProvider.Binance, CryptoFlowTypes.CardBuy, Apr9, 100m, "EUR", "Credit Card", fee: 2m),
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Linked.AddHours(3), 9_999m, "USD", "revolut"),
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9, 7m, "USD", "revolut", userId: Guid.NewGuid()));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Select(f => (f.Source, f.Flow.Class, f.Flow.AmountUsd, f.Fx)).Should().BeEquivalentTo(new[]
        {
            (CryptoExchangeProvider.RevolutX, FlowClass.ExternalInflow, 550.00m, FxStatus.Exact),
            (CryptoExchangeProvider.Binance, FlowClass.ExternalInflow, 110.00m, FxStatus.Exact),
            (CryptoExchangeProvider.Binance, FlowClass.Fee, 2.20m, FxStatus.Exact),
        });
        reading.Sources.Should().Equal(
            new FlowSourceCoverage(CryptoExchangeProvider.Binance, DateOnly.FromDateTime(Linked), false),
            new FlowSourceCoverage(CryptoExchangeProvider.RevolutX, DateOnly.FromDateTime(Linked), true));
    }

    [Fact]
    public async Task ADayWithNoPublishedRate_FallsBackToTheConverter()
    {
        _fx.Setup(f => f.ConvertToUsdAsync(500m, "EUR", DateOnly.FromDateTime(Apr9.AddDays(20)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConvertResult(540m, FxStatus.TodayRate));
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.RevolutX));
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9.AddDays(20), 500m, "EUR", "revolut"));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Should().ContainSingle().Which.Should().Be(
            new SourceFlow(CryptoExchangeProvider.RevolutX,
                new ClassifiedFlow(DateOnly.FromDateTime(Apr9.AddDays(20)), FlowClass.ExternalInflow, 540m), FxStatus.TodayRate));
    }

    [Fact]
    public async Task ACryptoLeg_IsValuedAtThePriceTakenWhenRead_FlaggedWhenThatWasNotItsOwnDay()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.RevolutX));
        db.CryptoFlowEvents.AddRange(
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, Apr9, 0.5m, "BTC", "external_crypto_wallet", priceUsd: 60000m),
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, Apr9.AddDays(-5), 0.1m, "BTC", "external_crypto_wallet",
                priceUsd: 60000m, recordedAt: Apr9));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Select(f => (f.Flow.AmountUsd, f.Fx)).Should().Equal((6000m, FxStatus.TodayRate), (30000m, FxStatus.Exact));
        reading.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnpricedCryptoLeg_OrAVenueNeverWalked_LeavesThatVenuesFlowsUnknown()
    {
        await using var db = NewDb();
        var otherUser = Guid.NewGuid();
        db.ExchangeCredentials.AddRange(Link(CryptoExchangeProvider.RevolutX), Link(CryptoExchangeProvider.RevolutX, walked: false, userId: otherUser));
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9, 0.5m, "DOGE", "external_crypto_wallet",
            isFiat: false));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);
        var neverWalked = await Sut(db).ReadAsync(otherUser, DateOnly.MinValue);

        reading.Flows.Should().BeEmpty();
        reading.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeFalse();
        neverWalked.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeFalse();
    }

    [Fact]
    public async Task FlowsOnOrBeforeFrom_AreLeftOut()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.RevolutX));
        db.CryptoFlowEvents.AddRange(
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9, 500m, "EUR", "revolut"),
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Apr9.AddDays(-1), 100m, "USD", "revolut"));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.FromDateTime(Apr9.AddDays(-1)));

        reading.Flows.Should().ContainSingle().Which.Flow.AmountUsd.Should().Be(550.00m);
    }
}

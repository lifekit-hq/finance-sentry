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
using Microsoft.Extensions.Time.Testing;
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

    private static ExchangeCredential Link(string provider, bool walked = true, Guid? userId = null, DateTime? chainWalkedTo = null)
    {
        var credential = ExchangeCredential.Create(userId ?? UserId, provider, [1], [1], [1], [1], [1], [1], 1);
        typeof(ExchangeCredential).GetProperty(nameof(ExchangeCredential.FirstLinkedAt))!.SetValue(credential, Linked);
        if (walked)
        {
            credential.MarkFlowsWalked(Apr9);
            credential.MarkChainFlowsWalked(chainWalkedTo ?? Apr9);
        }

        return credential;
    }

    private static CryptoFlowEvent Event(
        string provider, string type, DateTime at, decimal amount, string unit, string? counterparty,
        decimal? fee = null, decimal? priceUsd = null, DateTime? recordedAt = null, Guid? userId = null, bool? isFiat = null,
        string? txRef = null, string? network = null) =>
        CryptoFlowEvent.Create(userId ?? UserId, provider,
            new CryptoFlow(Guid.NewGuid().ToString(), type, at, amount, unit, isFiat ?? priceUsd is null, priceUsd, fee, fee is null ? null : unit,
                counterparty, "completed", txRef, network),
            recordedAt ?? at);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Apr9.AddDays(1)));

    private CryptoFlowReader Sut(CryptoSyncDbContext db) => new(db, _fx.Object, _time);

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
            new FlowSourceCoverage(CryptoExchangeProvider.Binance, DateOnly.FromDateTime(Linked), true),
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
    public async Task ARewardAndADustFee_AreIncomeAndFee_NeverMoneyPutIn()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.RevolutX));
        db.CryptoFlowEvents.AddRange(
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Reward, Apr9, 2m, "SOL", null, priceUsd: 150m, isFiat: false),
            Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.DustConversion, Apr9, 0.2m, "BNB", null, fee: 0.001m, priceUsd: 600m, isFiat: false));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Select(f => (f.Flow.Class, f.Flow.AmountUsd)).Should().BeEquivalentTo(new[]
        {
            (FlowClass.Income, 300m),
            (FlowClass.Fee, 0.6m),
        });
        reading.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnpricedReward_DropsOut_WithoutMakingTheVenuesFlowsUnknown()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.RevolutX));
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Reward, Apr9, 5m, "DOGE", null, isFiat: false));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Should().BeEmpty();
        reading.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeTrue();
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

    [Fact]
    public async Task AWalkMoreThanTheLagBehindNow_LeavesTheVenuesFlowsUnknown_AndOneWithinItDoesNot()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.RevolutX));
        await db.SaveChangesAsync();

        _time.SetUtcNow(new DateTimeOffset(Apr9) + CryptoFlowReader.CaughtUpLag - TimeSpan.FromMinutes(1));
        var caughtUp = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);
        _time.SetUtcNow(new DateTimeOffset(Apr9) + CryptoFlowReader.CaughtUpLag + TimeSpan.FromMinutes(1));
        var behind = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        caughtUp.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeTrue();
        behind.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeFalse();
    }

    [Fact]
    public async Task AReconnect_DoesNotMoveTheLinkDay_SoADepositFromBeforeItStaysInScope()
    {
        await using var db = NewDb();
        var credential = Link(CryptoExchangeProvider.RevolutX, walked: false);
        credential.Deactivate();
        credential.Reconnect([2], [2], [2], [2], [2], [2], 1);
        credential.MarkFlowsWalked(Apr9);
        db.ExchangeCredentials.Add(credential);
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, Linked.AddDays(10), 100m, "USD", "revolut"));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Should().ContainSingle().Which.Flow.AmountUsd.Should().Be(100m);
        reading.Sources.Should().ContainSingle().Which.LinkedOn.Should().Be(DateOnly.FromDateTime(Linked));
    }

    [Fact]
    public async Task Binance_WithItsFiatWalkedButNotItsCoinWalk_HasFlowsUnknown()
    {
        await using var db = NewDb();
        var binance = Link(CryptoExchangeProvider.Binance, walked: false);
        binance.MarkFlowsWalked(Apr9);
        db.ExchangeCredentials.Add(binance);
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Sources.Should().ContainSingle().Which.FlowsRecorded.Should().BeFalse();
    }

    private static readonly DateTime Mar3Send = new(2027, 3, 3, 23, 50, 0, DateTimeKind.Utc);
    private static readonly DateOnly Mar3 = DateOnly.FromDateTime(Mar3Send);
    private static readonly DateOnly Mar4 = Mar3.AddDays(1);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fixture12_AMoveBetweenVenues_IsOnlyItsFee_AndInTransitOverTheNight(bool enriched)
    {
        await using var db = NewDb();
        db.ExchangeCredentials.AddRange(Link(CryptoExchangeProvider.RevolutX), Link(CryptoExchangeProvider.Binance));
        db.CryptoFlowEvents.AddRange(
            enriched
                ? Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, Mar3Send, 10.00m, "SOL", "external_crypto_wallet",
                    fee: 0.05m, priceUsd: 150m, txRef: "H12", network: "SOLANA")
                // As S2a recorded it: what left the account, fee included, no hash.
                : Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, Mar3Send, 10.05m, "SOL", "external_crypto_wallet",
                    priceUsd: 150m),
            Event(CryptoExchangeProvider.Binance, CryptoFlowTypes.CryptoDeposit, Mar3Send.AddMinutes(30), 10.00m, "SOL", "on_chain",
                priceUsd: 150m, txRef: "H12", network: "SOL"));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Should().Equal(
            new SourceFlow(CryptoExchangeProvider.RevolutX, new ClassifiedFlow(Mar3, FlowClass.Fee, 7.50m), FxStatus.Exact));
        reading.InTransit.Should().Equal(new InTransitValue(CryptoExchangeProvider.RevolutX, Mar3, Mar4, 1500.00m, FxStatus.Exact));
        (reading.InTransitOn(Mar3), reading.InTransitOn(Mar4)).Should().Be((1500.00m, 0m));
        reading.UnmatchedTransferSources(DateOnly.MinValue, Mar4).Should().BeEmpty();
        reading.Sources.Should().OnlyContain(s => s.FlowsRecorded);
    }

    [Fact]
    public async Task Fixture13_AnUnmatchedCoinDeposit_IsAProvisionalInflow_FlaggedUnmatched()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.AddRange(Link(CryptoExchangeProvider.RevolutX), Link(CryptoExchangeProvider.Binance));
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.Binance, CryptoFlowTypes.CryptoDeposit, Apr9.AddHours(-2), 500m, "USDT",
            "on_chain", priceUsd: 1m, txRef: "0xfeed", network: "ETH"));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Should().Equal(new SourceFlow(CryptoExchangeProvider.Binance,
            new ClassifiedFlow(DateOnly.FromDateTime(Apr9), FlowClass.ExternalInflow, 500m), FxStatus.Exact, UnmatchedTransfer: true));
        reading.UnmatchedTransferSources(DateOnly.FromDateTime(Apr9).AddDays(-1), DateOnly.FromDateTime(Apr9))
            .Should().Equal(CryptoExchangeProvider.Binance);
    }

    [Fact]
    public async Task ACoinDeposit_WithNoOtherVenueLinked_IsExternal_AndNotFlagged()
    {
        await using var db = NewDb();
        db.ExchangeCredentials.Add(Link(CryptoExchangeProvider.Binance));
        db.CryptoFlowEvents.Add(Event(CryptoExchangeProvider.Binance, CryptoFlowTypes.CryptoDeposit, Apr9.AddHours(-2), 500m, "USDT",
            "on_chain", priceUsd: 1m));
        await db.SaveChangesAsync();

        var reading = await Sut(db).ReadAsync(UserId, DateOnly.MinValue);

        reading.Flows.Should().ContainSingle().Which.UnmatchedTransfer.Should().BeFalse();
    }

    [Fact]
    public async Task ASendNoVenueHasReceived_StaysInTransit_UntilTheOtherVenuesWalkReadsADayPastIt()
    {
        var sentAt = Apr9.AddHours(-2);
        var sentOn = DateOnly.FromDateTime(sentAt);
        CryptoFlowEvent Send() => Event(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, sentAt, 1m, "BTC", "external_crypto_wallet",
            fee: 0.0001m, priceUsd: 60000m, recordedAt: sentAt);

        await using var pending = NewDb();
        pending.ExchangeCredentials.AddRange(Link(CryptoExchangeProvider.RevolutX), Link(CryptoExchangeProvider.Binance));
        pending.CryptoFlowEvents.Add(Send());
        await pending.SaveChangesAsync();
        await using var settled = NewDb();
        settled.ExchangeCredentials.AddRange(
            Link(CryptoExchangeProvider.RevolutX),
            Link(CryptoExchangeProvider.Binance, chainWalkedTo: sentAt + CryptoTransferMatcher.ArrivalWindow));
        settled.CryptoFlowEvents.Add(Send());
        await settled.SaveChangesAsync();

        var inTransit = await Sut(pending).ReadAsync(UserId, DateOnly.MinValue);
        var external = await Sut(settled).ReadAsync(UserId, DateOnly.MinValue);

        inTransit.Flows.Select(f => (f.Flow.Class, f.Flow.AmountUsd)).Should().Equal((FlowClass.Fee, 6m));
        inTransit.InTransit.Should().Equal(new InTransitValue(CryptoExchangeProvider.RevolutX, sentOn, null, 60000m, FxStatus.Exact));
        external.InTransit.Should().BeEmpty();
        external.Flows.Select(f => (f.Flow.Class, f.Flow.AmountUsd, f.UnmatchedTransfer)).Should().Equal(
            (FlowClass.ExternalOutflow, 60000m, true), (FlowClass.Fee, 6m, false));
    }
}

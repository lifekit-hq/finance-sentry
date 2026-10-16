using System.Globalization;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

/// <summary>Cross-venue transfer matching (S2b; F2 tiers 1 and 2, fixtures 11, 12, 13 and 20).</summary>
public class CryptoTransferMatcherTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Mar3 = new(2027, 3, 3, 23, 50, 0, DateTimeKind.Utc);

    private static CryptoFlowEvent RevolutSend(decimal amount, decimal? fee = null, string? hash = null, string? network = null,
        DateTime? at = null, string unit = "SOL") =>
        Row(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, amount, unit, "external_crypto_wallet", at ?? Mar3, fee, hash, network);

    private static CryptoFlowEvent RevolutReceive(decimal amount, string? hash = null, DateTime? at = null, string unit = "SOL") =>
        Row(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Receive, amount, unit, "external_crypto_wallet", at ?? Mar3, null, hash, null);

    private static CryptoFlowEvent BinanceDeposit(decimal amount, string? hash = null, string? network = "SOL", DateTime? at = null,
        string unit = "SOL", string counterparty = "on_chain") =>
        Row(CryptoExchangeProvider.Binance, CryptoFlowTypes.CryptoDeposit, amount, unit, counterparty, at ?? Mar3.AddMinutes(30), null, hash, network);

    private static CryptoFlowEvent BinanceWithdrawal(decimal amount, decimal? fee = null, string? hash = null, DateTime? at = null,
        string unit = "SOL") =>
        Row(CryptoExchangeProvider.Binance, CryptoFlowTypes.CryptoWithdrawal, amount, unit, "on_chain", at ?? Mar3, fee, hash, "SOL");

    private static CryptoFlowEvent Row(string provider, string type, decimal amount, string unit, string counterparty, DateTime at,
        decimal? fee, string? hash, string? network) =>
        CryptoFlowEvent.Create(UserId, provider,
            new CryptoFlow(Guid.NewGuid().ToString(), type, at, amount, unit, false, 150m, fee, fee is null ? null : unit, counterparty,
                "completed", hash, network),
            at);

    [Fact]
    public void Fixture12_ASendAndADepositWithTheSameHash_AreOneMove_CostingTheDifference()
    {
        var send = RevolutSend(10.00m, fee: 0.05m, hash: "H12", network: "SOLANA");
        var deposit = BinanceDeposit(10.00m, hash: "H12");

        var pair = CryptoTransferMatcher.Match([send, deposit]).Should().ContainSingle().Subject;

        (pair.Sent, pair.Received).Should().Be((send, deposit));
        pair.Fee.Should().Be(0.05m);
    }

    [Fact]
    public void Fixture11_AHexHash_MatchesWhateverItsCaseOr0xPrefix()
    {
        var withdrawal = BinanceWithdrawal(1m, hash: "0xAB0000000000000000000000000000000000000000000000000000000000001", at: Mar3.AddDays(-40));
        var receive = RevolutReceive(1m, hash: "ab0000000000000000000000000000000000000000000000000000000000001");

        CryptoTransferMatcher.Match([withdrawal, receive]).Should().ContainSingle();
    }

    [Fact]
    public void ABase58Hash_IsCaseSensitive()
    {
        CryptoTransferMatcher.NormalizeHash("5VERv8NMvzbJ").Should().Be("5VERv8NMvzbJ");
        CryptoTransferMatcher.Match([RevolutSend(1m, hash: "5VERv8NMvzbJ"), BinanceDeposit(1m, hash: "5verv8nmvzbj")])
            .Should().BeEmpty("both name a hash, and they differ");
    }

    [Fact]
    public void AHashMatch_NeedsTheReceivedNoMoreThanTheSent()
    {
        CryptoTransferMatcher.Match([RevolutSend(1m, hash: "H"), BinanceDeposit(1.5m, hash: "H")]).Should().BeEmpty();
    }

    [Fact]
    public void WithoutAHash_ASendAndADepositWithinTheWindowAndTolerance_Match()
    {
        // An S2a Revolut X row: what left the account, fee included, no hash.
        var send = RevolutSend(10.05m);
        var deposit = BinanceDeposit(10.00m, hash: "H12");

        var pair = CryptoTransferMatcher.Match([send, deposit]).Should().ContainSingle().Subject;

        pair.Fee.Should().Be(0.05m);
    }

    [Theory]
    [InlineData(-61, false)]
    [InlineData(-59, true)]
    [InlineData(24 * 60 - 1, true)]
    [InlineData(24 * 60 + 1, false)]
    public void WithoutAHash_TheReceiptMustLandFromAnHourBeforeToADayAfterTheSend(int minutesAfterSend, bool matches)
    {
        var pairs = CryptoTransferMatcher.Match([RevolutSend(10m), BinanceDeposit(10m, at: Mar3.AddMinutes(minutesAfterSend))]);

        pairs.Should().HaveCount(matches ? 1 : 0);
    }

    [Theory]
    [InlineData("9.95", null, true)]
    [InlineData("9.94", null, false)]
    [InlineData("9.90", "0.05", true)]
    [InlineData("9.89", "0.05", false)]
    public void WithoutAHash_TheShortfallMustFitTheReportedFeePlusHalfAPercent(string received, string? fee, bool matches)
    {
        // 10 left the sending venue each time: the fee'd send lists 9.95 net and its 0.05 fee.
        decimal? reportedFee = fee is null ? null : decimal.Parse(fee, CultureInfo.InvariantCulture);
        var send = RevolutSend(10m - (reportedFee ?? 0m), fee: reportedFee);

        CryptoTransferMatcher.Match([send, BinanceDeposit(decimal.Parse(received, CultureInfo.InvariantCulture))])
            .Should().HaveCount(matches ? 1 : 0);
    }

    [Fact]
    public void Fixture20_TwoCandidates_AreNotGuessedBetween()
    {
        var send = RevolutSend(10m);
        var first = BinanceDeposit(10m, at: Mar3.AddMinutes(10));
        var second = BinanceDeposit(10m, at: Mar3.AddMinutes(20));

        CryptoTransferMatcher.Match([send, first, second]).Should().BeEmpty();
    }

    [Fact]
    public void AnotherUnit_AnotherNetwork_OrTheSameVenue_IsNoMatch()
    {
        CryptoTransferMatcher.Match([RevolutSend(10m), BinanceDeposit(10m, unit: "ETH")]).Should().BeEmpty();
        CryptoTransferMatcher.Match([RevolutSend(10m, network: "ETHEREUM", unit: "USDC"), BinanceDeposit(10m, network: "SOL", unit: "USDC")])
            .Should().BeEmpty();
        CryptoTransferMatcher.Match([BinanceWithdrawal(10m), BinanceDeposit(10m)]).Should().BeEmpty();
    }

    [Fact]
    public void AnInternalBinanceTransfer_OrAMoveWithTheOwnersRevolutApp_IsNeverATransferLeg()
    {
        var internalDeposit = BinanceDeposit(10m, counterparty: "binance_internal");
        var fromApp = Row(CryptoExchangeProvider.RevolutX, CryptoFlowTypes.Send, 10m, "SOL", "revolut", Mar3, null, null, null);

        CryptoTransferMatcher.Match([RevolutSend(10m), internalDeposit]).Should().BeEmpty();
        CryptoTransferMatcher.Match([fromApp, BinanceDeposit(10m)]).Should().BeEmpty();
    }

    [Fact]
    public void AnOffChainId_MatchesNoOtherVenue()
    {
        CryptoTransferMatcher.NormalizeHash("Off-chain transfer 6ad1").Should().BeNull();
        CryptoTransferMatcher.Match([RevolutSend(10m), BinanceDeposit(10m, hash: "Off-chain transfer 6ad1")]).Should().BeEmpty();
    }

    [Fact]
    public void Fixture13_AnUnmatchedDeposit_HasNoPair()
    {
        CryptoTransferMatcher.Match([BinanceDeposit(500m, unit: "USDT")]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("SOLANA", "SOL", true)]
    [InlineData("BITCOIN", "BTC", true)]
    [InlineData("ETHEREUM", "SOL", false)]
    [InlineData("SOME_NEW_CHAIN", "SOL", true)]
    [InlineData(null, "SOL", true)]
    public void Networks_AreComparedOnlyWhereBothAreKnown(string? revolut, string binance, bool compatible)
    {
        CryptoTransferMatcher.NetworksCompatible(revolut, binance).Should().Be(compatible);
    }
}

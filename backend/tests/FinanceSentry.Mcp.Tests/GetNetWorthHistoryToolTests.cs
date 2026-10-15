using System.Text.Json;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetNetWorthHistoryToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>> _handler = new();

    private GetNetWorthHistoryTool CreateSut() =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = UserId }, NullLogger<GetNetWorthHistoryTool>.Instance);

    // The three sleeves as the history query lists them, STALE_SOURCE on the named ones.
    private static IReadOnlyList<NetWorthSourceDto> Sleeves(params string[] stale) =>
    [
        .. new[] { ("banking", "Bank"), ("brokerage", "Brokerage"), ("crypto", "CryptoVenue") }.Select(s =>
            new NetWorthSourceDto(
                s.Item1, s.Item2, 100m,
                stale.Contains(s.Item1) ? HeadlineQuality.Degraded : HeadlineQuality.Complete,
                stale.Contains(s.Item1) ? ["STALE_SOURCE"] : [])),
    ];

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut().ExecuteAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenNoSnapshots()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([], false));

        var result = await CreateSut().ExecuteAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MapsSnapshots_PreservingOrder()
    {
        var snap1 = new NetWorthSnapshotDto(new DateOnly(2024, 1, 31), 1000m, 500m, 200m, 1700m, "USD", null, false);
        var snap2 = new NetWorthSnapshotDto(new DateOnly(2024, 2, 29), 1100m, 600m, 250m, 1950m, "USD", "brokerage", true);

        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([snap1, snap2], true));

        var result = await CreateSut().ExecuteAsync();

        result.Should().HaveCount(2);
        result[0].SnapshotDate.Should().Be(snap1.SnapshotDate);
        result[0].BankingTotal.Should().Be(1000m);
        result[0].BrokerageTotal.Should().Be(500m);
        result[0].CryptoTotal.Should().Be(200m);
        result[0].TotalNetWorth.Should().Be(1700m);
        result[0].Currency.Should().Be("USD");
        result[0].StaleSleeves.Should().BeNull();
        result[0].IsApproximate.Should().BeFalse();
        result[1].TotalNetWorth.Should().Be(1950m);
        result[1].StaleSleeves.Should().Be("brokerage");
        result[1].IsApproximate.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ExposesTheCashInvestedSplit_AndNullsForDaysWithoutOne()
    {
        var withSplit = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 2), 1000m, 500m, 250m, 1750m, "USD", null, false,
            CashTotal: 1150m, BrokerageInvested: 400m, CryptoInvested: 200m);
        var withoutSplit = new NetWorthSnapshotDto(new DateOnly(2026, 5, 1), 900m, 0m, 0m, 900m, "USD", null, false);

        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([withoutSplit, withSplit], true));

        var result = await CreateSut().ExecuteAsync();

        result[0].CashTotal.Should().BeNull();
        result[0].BrokerageInvested.Should().BeNull();
        result[0].CryptoInvested.Should().BeNull();
        result[1].CashTotal.Should().Be(1150m);
        result[1].BrokerageInvested.Should().Be(400m);
        result[1].CryptoInvested.Should().Be(200m);
    }

    [Fact]
    public async Task ExecuteAsync_TypesEachDaysTotal_AsAHeadline()
    {
        var measured = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 2), 1000m, 500m, 250m, 1750m, "USD", null, false, FxReasons: [], Sources: Sleeves());
        var reconstructed = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 3), 1000m, 500m, 250m, 1750m, "USD", "crypto", true, FxReasons: ["FX_TODAY_RATE"],
            Sources: Sleeves("crypto"));
        var unknownFx = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 4), 1000m, 500m, 250m, 1750m, "USD", null, false, FxReasons: ["FX_UNKNOWN_1TO1"],
            Sources: Sleeves());

        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([measured, reconstructed, unknownFx], true));

        var result = await CreateSut().ExecuteAsync();

        result[0].Headline!.Quality.Should().Be(HeadlineQuality.Complete);
        result[0].Headline!.Amount.Should().Be(1750m);
        result[0].Headline!.Window.To.Should().Be(new DateOnly(2026, 10, 2));
        result[1].Headline!.Quality.Should().Be(HeadlineQuality.Degraded);
        result[1].Headline!.Reasons.Should().Equal(
            new HeadlineReason(HeadlineReasonCode.StaleSource, "crypto"),
            new HeadlineReason(HeadlineReasonCode.BackfilledEstimate),
            new HeadlineReason(HeadlineReasonCode.FxTodayRate));
        result[2].Headline!.Quality.Should().Be(HeadlineQuality.Degraded);
        result[2].Headline!.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.FxUnknown1To1));
    }

    [Fact]
    public async Task ExecuteAsync_HeadlineContract_IsAdditiveAndTyped()
    {
        var row = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 3), 1000m, 500m, 250m, 1750m, "USD", "crypto", true, Sources: Sleeves("crypto"));
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([row], true));

        var result = await CreateSut().ExecuteAsync();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var entry = json.RootElement[0];

        // The existing fields stay where they were; the headline sits beside them.
        entry.GetProperty("totalNetWorth").GetDecimal().Should().Be(1750m);
        entry.GetProperty("staleSleeves").GetString().Should().Be("crypto");
        entry.GetProperty("isApproximate").GetBoolean().Should().BeTrue();
        var headline = entry.GetProperty("headline");
        headline.GetProperty("method").GetString().Should().Be("VALUE");
        headline.GetProperty("scope").GetString().Should().Be("umbrella");
        headline.GetProperty("quality").GetString().Should().Be("DEGRADED");
        headline.GetProperty("currency").GetString().Should().Be("USD");
        headline.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("code").GetString())
            .Should().Equal("STALE_SOURCE", "BACKFILLED_ESTIMATE", "FX_TODAY_RATE");
        var crypto = entry.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("sourceId").GetString() == "crypto");
        crypto.GetProperty("health").GetString().Should().Be("DEGRADED");
        crypto.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Equal("STALE_SOURCE");
    }

    [Fact]
    public async Task ExecuteAsync_TakesStaleSourcesFromThePerSourceReasons_OneReasonPerSource()
    {
        // A day whose rows name a source beyond the three sleeves: its STALE_SOURCE reaches the headline too,
        // while staleSleeves (the legacy value) still names only the sleeves.
        IReadOnlyList<NetWorthSourceDto> sources =
        [
            .. Sleeves("brokerage"),
            new NetWorthSourceDto("inzhur", "Fund", null, HeadlineQuality.Unavailable, ["STALE_SOURCE"]),
        ];
        var row = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 3), 1000m, 500m, 250m, 1750m, "USD", "brokerage", false, FxReasons: [], Sources: sources);
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([row], true));

        var result = await CreateSut().ExecuteAsync();

        result[0].StaleSleeves.Should().Be("brokerage");
        result[0].Sources.Should().BeSameAs(sources);
        result[0].Headline!.Quality.Should().Be(HeadlineQuality.Degraded);
        result[0].Headline!.Reasons.Should().Equal(
            new HeadlineReason(HeadlineReasonCode.StaleSource, "brokerage"),
            new HeadlineReason(HeadlineReasonCode.StaleSource, "inzhur"));
    }

    [Fact]
    public async Task ExecuteAsync_PassesDateBounds_ToQueryHandler()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 6, 30);

        GetNetWorthHistoryQuery? captured = null;
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetNetWorthHistoryQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new NetWorthHistoryResponse([], false));

        await CreateSut().ExecuteAsync(from, to);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(UserId);
        captured.From.Should().Be(from);
        captured.To.Should().Be(to);
    }
}

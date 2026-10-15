namespace FinanceSentry.Tests.Unit.Wealth;

using System.Text.Json;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetNetWorthHistoryQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ExposesTheSplitWhenPresent_AndNullsWhereTheDayHasNone()
    {
        var withSplit = new NetWorthSnapshot
        {
            UserId = UserId,
            SnapshotDate = new DateOnly(2026, 10, 2),
            BankingTotal = 1_000m,
            BrokerageTotal = 500m,
            CryptoTotal = 250m,
            TotalNetWorth = 1_750m,
            CashTotal = 1_150m,
            BrokerageInvested = 400m,
            CryptoInvested = 200m,
        };
        var withoutSplit = new NetWorthSnapshot
        {
            UserId = UserId,
            SnapshotDate = new DateOnly(2026, 5, 1),
            BankingTotal = 900m,
            TotalNetWorth = 900m,
        };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([withoutSplit, withSplit]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.Snapshots[0].CashTotal.Should().BeNull("a day before the split existed has no split, not zero");
        response.Snapshots[0].BrokerageInvested.Should().BeNull();
        response.Snapshots[0].CryptoInvested.Should().BeNull();
        response.Snapshots[1].CashTotal.Should().Be(1_150m);
        response.Snapshots[1].BrokerageInvested.Should().Be(400m);
        response.Snapshots[1].CryptoInvested.Should().Be(200m);
        response.Snapshots[1].TotalNetWorth.Should().Be(1_750m);
    }

    [Fact]
    public void ResponseJson_KeepsEveryExistingFieldAndAddsTheThreeNullableOnes()
    {
        var dto = new NetWorthSnapshotDto(new DateOnly(2026, 5, 1), 900m, 0m, 0m, 900m, "USD", null, false);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Contain([
            "snapshotDate", "bankingTotal", "brokerageTotal", "cryptoTotal", "totalNetWorth",
            "currency", "staleSleeves", "isApproximate",
            "cashTotal", "brokerageInvested", "cryptoInvested"]);
        json.RootElement.GetProperty("cashTotal").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Handle_NamesTheScopeChangesFromTheWindowStart()
    {
        var first = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 9, 1), TotalNetWorth = 1_000m };
        var last = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 10, 1), TotalNetWorth = 1_400m };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([first, last]);
        var reader = new Mock<IAccountScopeChangeReader>();
        var windowStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        reader.Setup(r => r.GetChangesSinceAsync(UserId, windowStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AccountScopeChange("monobank", AccountScopeChangeKinds.Linked, new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc))]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [reader.Object])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.ScopeChanges.Should().ContainSingle()
            .Which.Should().Be(new NetWorthScopeChangeDto("monobank", "linked", new DateOnly(2026, 9, 20)));
    }

    [Fact]
    public async Task Handle_ReportsNoScopeChangesWhenThereIsNoHistory()
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var reader = new Mock<IAccountScopeChangeReader>(MockBehavior.Strict);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [reader.Object])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.ScopeChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TypesTheWindowChange_AsAValueChangeHeadline()
    {
        var first = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 9, 1), TotalNetWorth = 1_000m };
        var last = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 10, 1), TotalNetWorth = 1_400m };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([first, last]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        var headline = response.ChangeHeadline!;
        headline.Method.Should().Be(HeadlineMethod.ValueChange);
        headline.Basis.Should().Be(HeadlineBasis.StartValue);
        headline.Amount.Should().Be(400m);
        headline.Percent.Should().Be(40m);
        headline.Window.Should().Be(new HeadlineWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)));
        headline.Quality.Should().Be(HeadlineQuality.Complete);
    }

    [Fact]
    public async Task Handle_WithholdsTheChangePercent_AcrossAScopeChange_AndCarriesTheEndRowsFlags()
    {
        var first = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 9, 1), TotalNetWorth = 1_000m, IsApproximate = true };
        var last = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 10, 1), TotalNetWorth = 1_400m, StaleSleeves = "brokerage" };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([first, last]);
        var reader = new Mock<IAccountScopeChangeReader>();
        reader.Setup(r => r.GetChangesSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AccountScopeChange("monobank", AccountScopeChangeKinds.Linked, new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc))]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [reader.Object])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        var headline = response.ChangeHeadline!;
        headline.Amount.Should().Be(400m, "the amount stays; only the percent is withheld");
        headline.Percent.Should().BeNull();
        headline.Quality.Should().Be(HeadlineQuality.Degraded);
        headline.Reasons.Select(r => r.Code).Should().Contain([
            HeadlineReasonCode.ScopeChanged, HeadlineReasonCode.BackfilledEstimate,
            HeadlineReasonCode.FxTodayRate, HeadlineReasonCode.StaleSource]);
    }

    [Fact]
    public async Task Handle_TypesASingleSnapshotWindow_AsUnavailableWithShortHistory()
    {
        var only = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 10, 1), TotalNetWorth = 1_400m };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([only]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        var headline = response.ChangeHeadline!;
        headline.Amount.Should().BeNull();
        headline.Percent.Should().BeNull();
        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Reasons.Select(r => r.Code).Should().Equal(HeadlineReasonCode.ShortHistory);
        headline.Window.Should().Be(new HeadlineWindow(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public async Task Handle_HasNoChangeHeadline_WithoutHistory()
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.ChangeHeadline.Should().BeNull();
    }
}

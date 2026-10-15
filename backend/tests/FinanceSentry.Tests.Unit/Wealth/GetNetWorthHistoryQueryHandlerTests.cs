namespace FinanceSentry.Tests.Unit.Wealth;

using System.Text.Json;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
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

    [Fact]
    public async Task Handle_OverAMixedWindow_ReadsTheRowsWhereTheyExist_AndKeepsEveryLegacyFigureAndTheChangeHeadline()
    {
        // The window starts before per-source rows were written (stale banking via the column only) and ends on a
        // day with rows whose crypto was carried forward: the row holds this run's 0, the column the carried 400.
        var old = new NetWorthSnapshot
        {
            Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = new DateOnly(2026, 9, 1),
            BankingTotal = 900m, BrokerageTotal = 300m, CryptoTotal = 100m, TotalNetWorth = 1_300m,
            StaleSleeves = "banking", IsApproximate = true,
        };
        var current = new NetWorthSnapshot
        {
            Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = new DateOnly(2026, 10, 1),
            BankingTotal = 1_000m, BrokerageTotal = 500m, CryptoTotal = 400m, TotalNetWorth = 1_900m,
            StaleSleeves = "crypto", FxStatus = FxStatus.Exact,
        };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([old, current]);
        repo.Setup(r => r.GetSourcesAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NetWorthSnapshotSource { SnapshotId = current.Id, SourceId = "banking", Kind = EconomyKind.Bank, Total = 1_000m, Health = SourceHealth.Complete },
                new NetWorthSnapshotSource { SnapshotId = current.Id, SourceId = "brokerage", Kind = EconomyKind.Brokerage, Total = 500m, Health = SourceHealth.Complete },
                new NetWorthSnapshotSource { SnapshotId = current.Id, SourceId = "crypto", Kind = EconomyKind.CryptoVenue, Total = 0m, Health = SourceHealth.Degraded, Reasons = [SourceReasons.StaleSource] },
            ]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.Snapshots.Select(s => (s.BankingTotal, s.BrokerageTotal, s.CryptoTotal, s.TotalNetWorth, s.StaleSleeves, s.IsApproximate))
            .Should().Equal((900m, 300m, 100m, 1_300m, "banking", true), (1_000m, 500m, 400m, 1_900m, "crypto", false));
        response.Snapshots[0].Sources!.Select(s => (s.SourceId, s.Total, s.Health)).Should().Equal(
            ("banking", 900m, HeadlineQuality.Degraded), ("brokerage", 300m, HeadlineQuality.Complete), ("crypto", 100m, HeadlineQuality.Complete));
        var crypto = response.Snapshots[1].Sources!.Single(s => s.SourceId == "crypto");
        (crypto.Total, crypto.Health, crypto.Kind).Should().Be((400m, HeadlineQuality.Degraded, "CryptoVenue"));
        crypto.Reasons.Should().Equal("STALE_SOURCE");

        var legacy = HeadlineFactory.Change(
            1_300m, 1_900m, HeadlineScope.Umbrella, old.SnapshotDate, current.SnapshotDate, false,
            HeadlineSignals.FromSnapshot(old.StaleSleeves, old.IsApproximate, old.FxStatus)
                .Merge(HeadlineSignals.FromSnapshot(current.StaleSleeves, current.IsApproximate, current.FxStatus)));
        response.ChangeHeadline.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void ResponseJson_AddsThePerSourceBreakdownBesideTheLegacyFields()
    {
        var dto = new NetWorthSnapshotDto(
            new DateOnly(2026, 10, 1), 900m, 0m, 0m, 900m, "USD", "banking", false,
            Sources: [new NetWorthSourceDto("banking", "Bank", 900m, HeadlineQuality.Degraded, ["STALE_SOURCE"])]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        json.RootElement.GetProperty("staleSleeves").GetString().Should().Be("banking");
        var source = json.RootElement.GetProperty("sources")[0];
        source.GetProperty("sourceId").GetString().Should().Be("banking");
        source.GetProperty("kind").GetString().Should().Be("Bank");
        source.GetProperty("total").GetDecimal().Should().Be(900m);
        source.GetProperty("reasons")[0].GetString().Should().Be("STALE_SOURCE");
    }
}

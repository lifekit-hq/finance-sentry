namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The stored total is every registered source's resolved figure (framework §5.6): the three sleeve columns resolve as
/// they always did, and a source registered after them is counted - and carried forward when its read fails.
/// </summary>
public class NetWorthSnapshotTotalTests
{
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly Day = new(2026, 10, 15);

    private static WealthSourceReading Reading(decimal cash, decimal invested = 0m)
        => new(cash, invested == 0m ? [] : [new BookFigurePosition("FUND", "fund", 1m, null, invested, "fund")]);

    private static BookSourceReading Fund(decimal? value)
        => new("fund", EconomyKind.Fund, value is null ? null : Reading(cash: 0m, invested: value.Value));

    private static BookSourceReading[] Sleeves() =>
    [
        new("banking", EconomyKind.Bank, Reading(cash: 100m)),
        new("brokerage", EconomyKind.Brokerage, Reading(cash: 10m, invested: 200m)),
        new("crypto", EconomyKind.CryptoVenue, Reading(cash: 0m, invested: 50m)),
    ];

    private static NetWorthSnapshotData Data(DateOnly day, IReadOnlyList<BookSourceReading>? sources, bool bankingFresh = true)
        => new(day, BankingTotal: 100m, BrokerageTotal: 210m, CryptoTotal: 50m, BankingFresh: bankingFresh, Sources: sources);

    private sealed class Store
    {
        public List<NetWorthSnapshot> Snapshots { get; } = [];

        public Dictionary<Guid, List<NetWorthSnapshotSource>> Rows { get; } = [];

        public Mock<INetWorthSnapshotRepository> Repository { get; } = new();

        public Store()
        {
            Repository
                .Setup(r => r.GetLatestBeforeUnscopedAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, DateOnly date, CancellationToken _) =>
                    Snapshots.Where(s => s.SnapshotDate < date).OrderBy(s => s.SnapshotDate).LastOrDefault());
            Repository
                .Setup(r => r.GetSourcesUnscopedAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    (IReadOnlyList<NetWorthSnapshotSource>)[.. ids.SelectMany(id => Rows.GetValueOrDefault(id) ?? [])]);
            Repository
                .Setup(r => r.UpsertAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback<NetWorthSnapshot, CancellationToken>((s, _) => Add(s, []))
                .Returns(Task.CompletedTask);
            Repository
                .Setup(r => r.UpsertWithSourcesAsync(
                    It.IsAny<NetWorthSnapshot>(), It.IsAny<IReadOnlyList<NetWorthSnapshotSource>>(), It.IsAny<CancellationToken>()))
                .Callback<NetWorthSnapshot, IReadOnlyList<NetWorthSnapshotSource>, CancellationToken>((s, r, _) => Add(s, r))
                .Returns(Task.CompletedTask);
            Repository
                .Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (IReadOnlyList<NetWorthSnapshot>)[.. Snapshots.OrderBy(s => s.SnapshotDate)]);
            Repository
                .Setup(r => r.GetSourcesAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    (IReadOnlyList<NetWorthSnapshotSource>)[.. ids.SelectMany(id => Rows.GetValueOrDefault(id) ?? [])]);
        }

        public async Task<NetWorthSnapshot> PersistAsync(NetWorthSnapshotData data)
        {
            await new NetWorthSnapshotService(Repository.Object).PersistSnapshotAsync(UserId, data);
            return Snapshots[^1];
        }

        public IReadOnlyList<NetWorthSnapshotSource> RowsOf(NetWorthSnapshot snapshot) => Rows[snapshot.Id];

        private void Add(NetWorthSnapshot snapshot, IReadOnlyList<NetWorthSnapshotSource> rows)
        {
            var stored = new NetWorthSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = snapshot.UserId,
                SnapshotDate = snapshot.SnapshotDate,
                BankingTotal = snapshot.BankingTotal,
                BrokerageTotal = snapshot.BrokerageTotal,
                CryptoTotal = snapshot.CryptoTotal,
                TotalNetWorth = snapshot.TotalNetWorth,
                StaleSleeves = snapshot.StaleSleeves,
                FxStatus = snapshot.FxStatus,
            };
            Snapshots.Add(stored);
            Rows[stored.Id] = [.. rows.Select(r => new NetWorthSnapshotSource
            {
                SnapshotId = stored.Id,
                SourceId = r.SourceId,
                Kind = r.Kind,
                Total = r.Total,
                Cash = r.Cash,
                Invested = r.Invested,
                Health = r.Health,
                Reasons = r.Reasons,
            })];
        }
    }

    [Fact]
    public async Task Total_OfTheThreeOriginalSources_IsTheSumOfTheSleeves_WithOrWithoutSourceFigures()
    {
        var withSources = new Store();
        var without = new Store();

        var a = await withSources.PersistAsync(Data(Day, Sleeves()));
        var b = await without.PersistAsync(Data(Day, null));

        a.TotalNetWorth.Should().Be(360m);
        b.TotalNetWorth.Should().Be(360m);
        a.StaleSleeves.Should().BeNull();
    }

    [Fact]
    public async Task Total_OfTheThreeOriginalSources_KeepsCarryingAStaleSleeveForwardFromItsColumn()
    {
        var store = new Store();
        await store.PersistAsync(Data(Day.AddDays(-1), Sleeves()));

        var carried = await store.PersistAsync(new NetWorthSnapshotData(
            Day, BankingTotal: 0m, BrokerageTotal: 210m, CryptoTotal: 50m, BankingFresh: false, Sources: Sleeves()));

        carried.BankingTotal.Should().Be(100m);
        carried.TotalNetWorth.Should().Be(360m);
        carried.StaleSleeves.Should().Be("banking");
    }

    [Fact]
    public async Task Total_CountsAFourthSource_AndWritesItsRow()
    {
        var store = new Store();

        var snapshot = await store.PersistAsync(Data(Day, [.. Sleeves(), Fund(700m)]));

        snapshot.TotalNetWorth.Should().Be(1_060m);
        snapshot.BankingTotal.Should().Be(100m);
        snapshot.BrokerageTotal.Should().Be(210m);
        snapshot.CryptoTotal.Should().Be(50m);
        store.RowsOf(snapshot).Single(r => r.SourceId == "fund").Total.Should().Be(700m);
        store.RowsOf(snapshot).Sum(r => r.Total!.Value).Should().Be(snapshot.TotalNetWorth,
            "the per-source rows add up to the stored total");
    }

    [Fact]
    public async Task Total_CarriesAFourthSourceForward_FromItsOwnPreviousRow_WhenItsReadFails()
    {
        var store = new Store();
        await store.PersistAsync(Data(Day.AddDays(-2), [.. Sleeves(), Fund(700m)]));

        var failed = await store.PersistAsync(Data(Day.AddDays(-1), [.. Sleeves(), Fund(null)]));
        var failedAgain = await store.PersistAsync(Data(Day, [.. Sleeves(), Fund(null)]));

        failed.TotalNetWorth.Should().Be(1_060m, "a bad read does not drop the source's whole value from the total");
        failedAgain.TotalNetWorth.Should().Be(1_060m, "a carried figure is itself carried on the next failure");
        var row = store.RowsOf(failedAgain).Single(r => r.SourceId == "fund");
        row.Total.Should().Be(700m);
        row.Health.Should().Be(SourceHealth.Degraded);
        row.Reasons.Should().Contain(SourceReasons.StaleSource);
        failedAgain.StaleSleeves.Should().BeNull("the legacy column names only the three sleeves");
    }

    [Fact]
    public async Task Total_LeavesAFourthSourceOut_WhenItsReadFailsAndItHasNoEarlierFigure()
    {
        var store = new Store();
        await store.PersistAsync(Data(Day.AddDays(-1), Sleeves()));

        var snapshot = await store.PersistAsync(Data(Day, [.. Sleeves(), Fund(null)]));

        snapshot.TotalNetWorth.Should().Be(360m);
        var row = store.RowsOf(snapshot).Single(r => r.SourceId == "fund");
        row.Health.Should().Be(SourceHealth.Unavailable);
        row.Total.Should().BeNull();
    }

    [Fact]
    public async Task History_IncludesAFourthSource_InTheRowsAndTheChangeHeadline()
    {
        var store = new Store();
        await store.PersistAsync(Data(Day.AddDays(-1), [.. Sleeves(), Fund(700m)]));
        await store.PersistAsync(Data(Day, [.. Sleeves(), Fund(900m)]));

        var response = await new GetNetWorthHistoryQueryHandler(store.Repository.Object, [])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.Snapshots.Select(s => s.TotalNetWorth).Should().Equal(1_060m, 1_260m);
        response.Snapshots[1].Sources!.Single(s => s.SourceId == "fund").Total.Should().Be(900m);
        response.ChangeHeadline!.Amount.Should().Be(200m, "the fourth source's growth is part of the headline change");
    }
}

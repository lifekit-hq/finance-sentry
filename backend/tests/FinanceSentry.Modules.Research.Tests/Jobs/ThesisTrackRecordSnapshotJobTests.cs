namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ThesisTrackRecordSnapshotJobTests
{
    [Fact]
    public async Task ExecuteAsync_BackfillsPendingEvent_WhenQuoteBecomesAvailable()
    {
        var pendingEvent = new ThesisEvent
        {
            UserId = Guid.NewGuid(),
            SubjectType = ThesisSubjectType.Thesis,
            SubjectId = Guid.NewGuid(),
            Ticker = "MU",
            EventType = ThesisEventType.Created,
            PricesPending = true,
        };

        var eventRepo = new Mock<IThesisEventRepository>();
        eventRepo.Setup(r => r.GetUserIdsWithEventsUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[]);
        eventRepo.Setup(r => r.ListPendingUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([pendingEvent]);
        eventRepo.Setup(r => r.ListUnscopedAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ThesisEvent>)[]);

        ThesisEvent? updated = null;
        eventRepo.Setup(r => r.UpdatePricesAsync(It.IsAny<ThesisEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThesisEvent, CancellationToken>((e, _) => updated = e)
            .Returns(Task.CompletedTask);

        var thesisRepo = new Mock<IThesisRepository>();
        thesisRepo.Setup(r => r.GetUserIdsWithThesesUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[]);

        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>
            {
                ["MU"] = new() { Ticker = "MU", Price = 105m },
                ["SPY"] = new() { Ticker = "SPY", Price = 505m },
            });

        var sut = new ThesisTrackRecordSnapshotJob(
            eventRepo.Object, thesisRepo.Object, marketData.Object, Mock.Of<IBenchmarkTrackRecordMaterializer>(),
            NullLogger<ThesisTrackRecordSnapshotJob>.Instance);

        await sut.ExecuteAsync();

        updated.Should().NotBeNull();
        updated!.PricesPending.Should().BeFalse();
        updated.SubjectPrice.Should().Be(105m);
        updated.BenchmarkPrice.Should().Be(505m);
    }

    [Fact]
    public async Task ExecuteAsync_AppendsSnapshotEvent_ForActiveThesisWithNoTerminalEvent()
    {
        var userId = Guid.NewGuid();
        var thesis = new InvestmentThesis { UserId = userId, Ticker = "MU" };

        var eventRepo = new Mock<IThesisEventRepository>();
        eventRepo.Setup(r => r.GetUserIdsWithEventsUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[]);
        eventRepo.Setup(r => r.ListPendingUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ThesisEvent>)[]);
        eventRepo.Setup(r => r.ListUnscopedAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ThesisEvent>)[
                new ThesisEvent
                {
                    UserId = userId,
                    SubjectId = thesis.Id,
                    SubjectType = ThesisSubjectType.Thesis,
                    Ticker = "MU",
                    EventType = ThesisEventType.Created,
                },
            ]);

        ThesisEvent? appended = null;
        eventRepo.Setup(r => r.AppendAsync(It.IsAny<ThesisEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThesisEvent, CancellationToken>((e, _) => appended = e)
            .Returns(Task.CompletedTask);

        var thesisRepo = new Mock<IThesisRepository>();
        thesisRepo.Setup(r => r.GetUserIdsWithThesesUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[userId]);
        thesisRepo.Setup(r => r.ListUnscopedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<InvestmentThesis>)[thesis]);

        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>
            {
                ["MU"] = new() { Ticker = "MU", Price = 110m },
                ["SPY"] = new() { Ticker = "SPY", Price = 515m },
            });

        var sut = new ThesisTrackRecordSnapshotJob(
            eventRepo.Object, thesisRepo.Object, marketData.Object, Mock.Of<IBenchmarkTrackRecordMaterializer>(),
            NullLogger<ThesisTrackRecordSnapshotJob>.Instance);

        await sut.ExecuteAsync();

        appended.Should().NotBeNull();
        appended!.EventType.Should().Be(ThesisEventType.Snapshot);
        appended.SubjectId.Should().Be(thesis.Id);
        appended.PricesPending.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_SkipsSnapshot_ForThesisWithTerminalEvent()
    {
        var userId = Guid.NewGuid();
        var thesis = new InvestmentThesis { UserId = userId, Ticker = "MU" };

        var eventRepo = new Mock<IThesisEventRepository>();
        eventRepo.Setup(r => r.GetUserIdsWithEventsUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[]);
        eventRepo.Setup(r => r.ListPendingUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ThesisEvent>)[]);
        eventRepo.Setup(r => r.ListUnscopedAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ThesisEvent>)[
                new ThesisEvent
                {
                    UserId = userId,
                    SubjectId = thesis.Id,
                    SubjectType = ThesisSubjectType.Thesis,
                    Ticker = "MU",
                    EventType = ThesisEventType.Closed,
                },
            ]);

        var thesisRepo = new Mock<IThesisRepository>();
        thesisRepo.Setup(r => r.GetUserIdsWithThesesUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[userId]);
        thesisRepo.Setup(r => r.ListUnscopedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<InvestmentThesis>)[thesis]);

        var marketData = new Mock<IMarketDataService>();

        var sut = new ThesisTrackRecordSnapshotJob(
            eventRepo.Object, thesisRepo.Object, marketData.Object, Mock.Of<IBenchmarkTrackRecordMaterializer>(),
            NullLogger<ThesisTrackRecordSnapshotJob>.Instance);

        await sut.ExecuteAsync();

        eventRepo.Verify(
            r => r.AppendAsync(It.IsAny<ThesisEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MaterializesRelativeTrackRecord_PerUserWithEvents_AndIsolatesFailures()
    {
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();

        var eventRepo = new Mock<IThesisEventRepository>();
        eventRepo.Setup(r => r.ListPendingUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ThesisEvent>)[]);
        eventRepo.Setup(r => r.GetUserIdsWithEventsUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[failing, healthy]);

        var thesisRepo = new Mock<IThesisRepository>();
        thesisRepo.Setup(r => r.GetUserIdsWithThesesUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[]);

        var trackRecord = new Mock<IBenchmarkTrackRecordMaterializer>();
        trackRecord.Setup(t => t.MaterializeAsync(failing, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new ThesisTrackRecordSnapshotJob(
            eventRepo.Object, thesisRepo.Object, Mock.Of<IMarketDataService>(), trackRecord.Object,
            NullLogger<ThesisTrackRecordSnapshotJob>.Instance);

        await sut.ExecuteAsync();

        trackRecord.Verify(t => t.MaterializeAsync(healthy, It.IsAny<CancellationToken>()), Times.Once);
    }
}

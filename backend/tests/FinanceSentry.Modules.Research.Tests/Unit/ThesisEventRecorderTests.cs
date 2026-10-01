namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ThesisEventRecorderTests
{
    [Fact]
    public async Task RecordAsync_NeverThrows_AndMarksPricesPending_WhenMarketDataServiceThrows()
    {
        var repo = new Mock<IThesisEventRepository>();
        repo.Setup(r => r.GetLatestForSubjectUnscopedAsync(
                It.IsAny<Guid>(), It.IsAny<ThesisSubjectType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThesisEvent?)null);

        ThesisEvent? appended = null;
        repo.Setup(r => r.AppendAsync(It.IsAny<ThesisEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThesisEvent, CancellationToken>((e, _) => appended = e)
            .Returns(Task.CompletedTask);

        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Yahoo is down"));

        var sut = new ThesisEventRecorder(repo.Object, marketData.Object, NullLogger<ThesisEventRecorder>.Instance);

        var act = async () => await sut.RecordAsync(
            Guid.NewGuid(), ThesisSubjectType.Thesis, Guid.NewGuid(), "MU", ThesisEventType.Created);

        await act.Should().NotThrowAsync();

        appended.Should().NotBeNull();
        appended!.PricesPending.Should().BeTrue();
        appended.SubjectPrice.Should().BeNull();
        appended.BenchmarkPrice.Should().BeNull();
    }

    [Fact]
    public async Task RecordAsync_MarksPricesPending_WhenQuoteIsMissingFromResult()
    {
        var repo = new Mock<IThesisEventRepository>();
        repo.Setup(r => r.GetLatestForSubjectUnscopedAsync(
                It.IsAny<Guid>(), It.IsAny<ThesisSubjectType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThesisEvent?)null);

        ThesisEvent? appended = null;
        repo.Setup(r => r.AppendAsync(It.IsAny<ThesisEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThesisEvent, CancellationToken>((e, _) => appended = e)
            .Returns(Task.CompletedTask);

        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>()); // neither ticker resolved

        var sut = new ThesisEventRecorder(repo.Object, marketData.Object, NullLogger<ThesisEventRecorder>.Instance);

        await sut.RecordAsync(Guid.NewGuid(), ThesisSubjectType.Thesis, Guid.NewGuid(), "MU", ThesisEventType.Created);

        appended.Should().NotBeNull();
        appended!.PricesPending.Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_AppendsExactlyOneCreatedEvent_WhenCalledTwiceForSameSubject()
    {
        var subjectId = Guid.NewGuid();
        ThesisEvent? existingCreated = null;

        var repo = new Mock<IThesisEventRepository>();
        repo.Setup(r => r.GetLatestForSubjectUnscopedAsync(
                It.IsAny<Guid>(), ThesisSubjectType.Thesis, subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => existingCreated);

        var appendCount = 0;
        repo.Setup(r => r.AppendAsync(It.IsAny<ThesisEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThesisEvent, CancellationToken>((e, _) =>
            {
                appendCount++;
                existingCreated = e;
            })
            .Returns(Task.CompletedTask);

        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>
            {
                ["MU"] = new() { Ticker = "MU", Price = 100m },
                ["SPY"] = new() { Ticker = "SPY", Price = 500m },
            });

        var sut = new ThesisEventRecorder(repo.Object, marketData.Object, NullLogger<ThesisEventRecorder>.Instance);

        await sut.RecordAsync(Guid.NewGuid(), ThesisSubjectType.Thesis, subjectId, "MU", ThesisEventType.Created);
        await sut.RecordAsync(Guid.NewGuid(), ThesisSubjectType.Thesis, subjectId, "MU", ThesisEventType.Created);

        appendCount.Should().Be(1);
    }
}

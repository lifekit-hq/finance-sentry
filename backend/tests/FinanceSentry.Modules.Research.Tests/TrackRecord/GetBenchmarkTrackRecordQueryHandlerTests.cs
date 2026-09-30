namespace FinanceSentry.Modules.Research.Tests.TrackRecord;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class GetBenchmarkTrackRecordQueryHandlerTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IBenchmarkRelativeRecordRepository> records = new();

    private GetBenchmarkTrackRecordQueryHandler Sut() =>
        new(records.Object, Options.Create(new RelativePerformanceConfig()));

    private void Seed(params BenchmarkRelativeRecord[] rows)
        => records.Setup(r => r.ListLatestRunAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    private static BenchmarkRelativeRecord Row(TrackRecordScope scope, string key, string label, TrackRecordWindow window)
        => new()
        {
            UserId = UserId,
            AsOf = AsOf,
            Scope = scope,
            ScopeKey = key,
            Label = label,
            Window = window,
            Covered = true,
            ExcessReturnPct = 1m,
        };

    private static BenchmarkRelativeRecord[] Run() =>
    [
        Row(TrackRecordScope.Thesis, "a", "MU", TrackRecordWindow.OneMonth),
        Row(TrackRecordScope.Thesis, "b", "NVDA", TrackRecordWindow.OneMonth),
        Row(TrackRecordScope.Book, "book", "Book", TrackRecordWindow.OneMonth),
        Row(TrackRecordScope.Book, "book", "Book", TrackRecordWindow.ThreeMonths),
        Row(TrackRecordScope.Sleeve, "Equity", "Equity", TrackRecordWindow.OneMonth),
    ];

    [Fact]
    public async Task EmptyStore_SaysNotMaterializedYet_AndCarriesTheRule()
    {
        Seed();

        var result = await Sut().Handle(new GetBenchmarkTrackRecordQuery(UserId, null, null, null), CancellationToken.None);

        result.Rows.Should().BeEmpty();
        result.AsOf.Should().BeNull();
        result.Note.Should().Contain("Not materialized yet");
        result.Rule.ThresholdPct.Should().Be(5m);
        result.Rule.SustainedRuns.Should().Be(4);
        result.Rule.Window.Should().Be("ThreeMonths");
    }

    [Fact]
    public async Task NoFilter_ReturnsTheWholeRun_OrderedByScope()
    {
        Seed(Run());

        var result = await Sut().Handle(new GetBenchmarkTrackRecordQuery(UserId, null, null, null), CancellationToken.None);

        result.AsOf.Should().Be(AsOf);
        result.Note.Should().BeNull();
        result.Rows.Select(r => (r.Scope, r.Label, r.Window)).Should().Equal(
            ("Book", "Book", "OneMonth"),
            ("Book", "Book", "ThreeMonths"),
            ("Sleeve", "Equity", "OneMonth"),
            ("Thesis", "MU", "OneMonth"),
            ("Thesis", "NVDA", "OneMonth"));
    }

    [Theory]
    [InlineData("3M")]
    [InlineData("threemonths")]
    public async Task WindowFilter_AcceptsAliasesAndEnumNames(string window)
    {
        Seed(Run());

        var result = await Sut().Handle(new GetBenchmarkTrackRecordQuery(UserId, "book", window, null), CancellationToken.None);

        result.Rows.Should().ContainSingle().Which.Window.Should().Be("ThreeMonths");
    }

    [Fact]
    public async Task TickerFilter_NarrowsThesisRows_AndKeepsAggregates()
    {
        Seed(Run());

        var result = await Sut().Handle(new GetBenchmarkTrackRecordQuery(UserId, null, "1M", "mu"), CancellationToken.None);

        result.Rows.Select(r => r.Label).Should().Equal("Book", "Equity", "MU");
    }

    [Theory]
    [InlineData("Portfolio", null)]
    [InlineData(null, "6M")]
    public async Task UnknownFilter_ReturnsANote_WithoutReadingTheStore(string? scope, string? window)
    {
        var result = await Sut().Handle(new GetBenchmarkTrackRecordQuery(UserId, scope, window, null), CancellationToken.None);

        result.Rows.Should().BeEmpty();
        result.Note.Should().StartWith("Unknown");
        records.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FilterMatchingNothing_SaysSo()
    {
        Seed(Run());

        var result = await Sut().Handle(new GetBenchmarkTrackRecordQuery(UserId, "thesis", "1Y", null), CancellationToken.None);

        result.Rows.Should().BeEmpty();
        result.Note.Should().Be("No stored rows match the filter.");
    }
}

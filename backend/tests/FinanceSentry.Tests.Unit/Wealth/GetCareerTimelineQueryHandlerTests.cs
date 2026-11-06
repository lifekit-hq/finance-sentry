namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

public sealed class GetCareerTimelineQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateOnly Today = new(2026, 11, 6);

    private readonly FakeCareerEventRepository _events = new();

    private GetCareerTimelineQueryHandler Handler() => new(_events, new FixedClock(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)));

    private void Seed()
    {
        _events.Events.Add(CareerEvent.Create(UserId, CareerEventKind.IncomeStep, Today.AddDays(120), null, "Promotion", 500m, "EUR", "after review"));
        _events.Events.Add(CareerEvent.Create(UserId, CareerEventKind.OneOffIn, Today.AddDays(-10), null, "Bonus", 2_000m, "USD", null));
        var retired = CareerEvent.Create(UserId, CareerEventKind.OneOffOut, Today.AddDays(200), null, "Course", 900m, "USD", null);
        retired.Retire(DateTimeOffset.UtcNow);
        _events.Events.Add(retired);
    }

    [Fact]
    public async Task OnTheApiHost_ReturnsEveryFieldInDateOrder_AndFlagsWhatCounts()
    {
        Seed();

        var result = await Handler().Handle(new GetCareerTimelineQuery(UserId), CancellationToken.None);

        result.Readable.Should().BeTrue();
        result.Events.Select(e => e.Label).Should().Equal("Bonus", "Promotion", "Course");
        result.Events.Select(e => e.Counted).Should().Equal(false, true, false);
        result.Events.Single(e => e.Label == "Course").Retired.Should().BeTrue();
        result.Events.Single(e => e.Label == "Promotion").Amount.Should().Be(500m);
        result.Events.Single(e => e.Label == "Promotion").Note.Should().Be("after review");
        result.CountedEvents.Should().Be(1);
        result.Label.Should().Contain("not a forecast");
    }

    [Fact]
    public async Task ALeaveUnderWay_AppearsAsCounted_AndAnEndedOneDoesNot()
    {
        _events.Events.Add(CareerEvent.Create(UserId, CareerEventKind.Leave, Today.AddDays(-14), Today.AddDays(90), "Parental", 3_000m, "USD", null));
        _events.Events.Add(CareerEvent.Create(UserId, CareerEventKind.Leave, Today.AddDays(-120), Today.AddDays(-30), "Sabbatical", 3_000m, "USD", null));

        var result = await Handler().Handle(new GetCareerTimelineQuery(UserId), CancellationToken.None);

        result.Events.Single(e => e.Label == "Parental").Counted.Should().BeTrue();
        result.Events.Single(e => e.Label == "Sabbatical").Counted.Should().BeFalse();
        result.CountedEvents.Should().Be(1);
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_ReturnsOnlyThePlainColumns()
    {
        _events.CanReadEncryptedFields = false;
        Seed();

        var result = await Handler().Handle(new GetCareerTimelineQuery(UserId), CancellationToken.None);

        result.Readable.Should().BeFalse();
        result.Events.Should().HaveCount(3);
        result.Events.Should().OnlyContain(e => e.Label == null && e.Amount == null && e.Note == null);
        result.Events.Select(e => e.Kind).Should().Equal(CareerEventKind.OneOffIn, CareerEventKind.IncomeStep, CareerEventKind.OneOffOut);
        result.CountedEvents.Should().Be(1, "the counting rule needs no encrypted column");
    }

    [Fact]
    public async Task AnotherPersonsEvents_AreNotReturned()
    {
        Seed();

        var result = await Handler().Handle(new GetCareerTimelineQuery(Guid.NewGuid()), CancellationToken.None);

        result.Events.Should().BeEmpty();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

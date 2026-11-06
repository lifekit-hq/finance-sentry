namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Which career events shape a scenario (#454 S5): money-semantics §19. Synthetic figures.</summary>
public sealed class ScenarioReaderTests
{
    private static readonly Guid UserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Other = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateOnly Today = new(2026, 9, 26);

    private readonly FakeCareerEventRepository _events = new();

    private ScenarioReader Reader() => new(_events, new FixedClock(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero)));

    private CareerEvent Add(
        CareerEventKind kind, DateOnly date, decimal amount, string currency = "USD", DateOnly? end = null, Guid? user = null)
    {
        var career = CareerEvent.Create(user ?? UserId, kind, date, end, "Event", amount, currency, null);
        _events.Events.Add(career);
        return career;
    }

    [Fact]
    public async Task NoEvents_IsNone()
    {
        var scenario = await Reader().ReadAsync(UserId, CancellationToken.None);

        scenario.Status.Should().Be(ScenarioStatus.None);
        scenario.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyFutureUnretiredEvents_AreCounted_AndANotYetStartedOneStillCountsAtItsDate()
    {
        Add(CareerEventKind.IncomeStep, Today.AddDays(60), 500m);
        Add(CareerEventKind.IncomeStep, Today.AddDays(-60), 900m);
        Add(CareerEventKind.OneOffIn, Today.AddDays(30), 2_000m).Retire(DateTimeOffset.UtcNow);

        var scenario = await Reader().ReadAsync(UserId, CancellationToken.None);

        scenario.Status.Should().Be(ScenarioStatus.Applied);
        scenario.Events.Should().ContainSingle().Which.Should().Be(
            new ScenarioEvent(ScenarioEventKind.IncomeStep, Today.AddDays(60), null, 500m));
    }

    [Fact]
    public async Task OnlyPastOrRetiredEvents_ClaimNoScenario()
    {
        Add(CareerEventKind.OneOffOut, Today.AddDays(-1), 100m);
        Add(CareerEventKind.IncomeStep, Today.AddDays(10), 100m).Retire(DateTimeOffset.UtcNow);

        (await Reader().ReadAsync(UserId, CancellationToken.None)).Status.Should().Be(ScenarioStatus.None);
    }

    [Fact]
    public async Task AnotherPersonsEvents_AreNeverRead()
    {
        Add(CareerEventKind.IncomeStep, Today.AddDays(60), 500m, user: Other);

        (await Reader().ReadAsync(UserId, CancellationToken.None)).Status.Should().Be(ScenarioStatus.None);
    }

    [Fact]
    public async Task AmountsAreConvertedToUsdOnce_AtTheRead()
    {
        Add(CareerEventKind.IncomeStep, Today.AddDays(60), 1_000m, "EUR");

        var scenario = await Reader().ReadAsync(UserId, CancellationToken.None);

        scenario.Events.Single().AmountUsd.Should().Be(Math.Round(FinanceSentry.Core.Utils.CurrencyConverter.ToUsd(1_000m, "EUR"), 2));
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_CountedEventsAreUnreadable_AndNoAmountIsTouched()
    {
        _events.CanReadEncryptedFields = false;
        Add(CareerEventKind.IncomeStep, Today.AddDays(60), 500m);

        var scenario = await Reader().ReadAsync(UserId, CancellationToken.None);

        scenario.Status.Should().Be(ScenarioStatus.Unreadable);
        scenario.Events.Should().BeEmpty("no scenario figure is claimed from events that cannot be read");
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_PastOrRetiredEventsAreNoScenarioAtAll()
    {
        _events.CanReadEncryptedFields = false;
        Add(CareerEventKind.IncomeStep, Today.AddDays(-5), 500m);
        Add(CareerEventKind.OneOffIn, Today.AddDays(40), 500m).Retire(DateTimeOffset.UtcNow);

        (await Reader().ReadAsync(UserId, CancellationToken.None)).Status.Should().Be(ScenarioStatus.None);
    }

    [Fact]
    public async Task Deltas_AreBuiltAgainstTheDayTheEventsWereCountedOn()
    {
        Add(CareerEventKind.IncomeStep, new DateOnly(2026, 12, 1), 500m);

        var scenario = await Reader().ReadAsync(UserId, CancellationToken.None);

        scenario.Deltas(4).Should().Equal(0m, 0m, 500m, 500m);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

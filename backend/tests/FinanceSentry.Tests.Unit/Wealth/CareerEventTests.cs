namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Validators;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>A career event (#454 S5): what counts toward a scenario, how it retires, and what it accepts. Synthetic figures.</summary>
public sealed class CareerEventTests
{
    private static readonly Guid UserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));

    private static CareerEvent Event(
        CareerEventKind kind = CareerEventKind.IncomeStep, DateOnly? date = null, DateOnly? end = null, decimal amount = 500m, string? note = null)
        => CareerEvent.Create(UserId, kind, date ?? new DateOnly(2027, 3, 1), end, " Promotion ", amount, "eur", note);

    [Fact]
    public void Create_TrimsTheLabel_UppercasesTheCurrency_AndKeepsAnEmptyNoteAsNull()
    {
        var career = Event(note: "  ");

        career.Label.Should().Be("Promotion");
        career.Currency.Should().Be("EUR");
        career.Note.Should().BeNull();
        career.UserId.Should().Be(UserId);
    }

    [Fact]
    public void Update_OnlyALeaveKeepsAnEndDate()
    {
        var career = Event(CareerEventKind.Leave, new DateOnly(2027, 6, 1), new DateOnly(2027, 8, 31), 1_000m);
        career.EndDate.Should().Be(new DateOnly(2027, 8, 31));

        career.Update(CareerEventKind.OneOffIn, new DateOnly(2027, 6, 1), new DateOnly(2027, 8, 31), "Bonus", 2_000m, "USD", null);

        career.EndDate.Should().BeNull("a one-off has no last day");
    }

    [Fact]
    public void IsCounted_IsTrueOnlyForAnUnretiredEventAfterToday()
    {
        Event(date: Today.AddDays(1)).IsCounted(Today).Should().BeTrue();
        Event(date: Today).IsCounted(Today).Should().BeFalse("an event that has happened is in the baseline's history");
        Event(date: Today.AddDays(-30)).IsCounted(Today).Should().BeFalse();
    }

    [Fact]
    public void Retire_TakesTheEventOutOfTheScenario_AndKeepsTheFirstRetirement()
    {
        var career = Event(date: Today.AddDays(90));
        var first = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

        career.Retire(first);
        career.Retire(first.AddDays(5));

        career.RetiredAt.Should().Be(first);
        career.IsCounted(Today).Should().BeFalse("a retired event never counts");
    }

    [Fact]
    public void Update_ClearsTheRetirement_BecauseAnEditRestatesTheEvent()
    {
        var career = Event(date: Today.AddDays(90));
        career.Retire(DateTimeOffset.UtcNow);

        career.Update(CareerEventKind.IncomeStep, Today.AddDays(90), null, "Promotion", 600m, "EUR", null);

        career.RetiredAt.Should().BeNull();
        career.IsCounted(Today).Should().BeTrue();
    }

    [Fact]
    public void Outline_AppliesTheSameCountingRuleAsTheEntity()
    {
        var retired = new FinanceSentry.Modules.Wealth.Domain.Repositories.CareerEventOutline(
            Guid.NewGuid(), CareerEventKind.OneOffIn, Today.AddDays(30), null, "USD", DateTimeOffset.UtcNow);
        var open = retired with { RetiredAt = null };
        var past = open with { Date = Today.AddDays(-1) };

        retired.IsCounted(Today).Should().BeFalse();
        open.IsCounted(Today).Should().BeTrue();
        past.IsCounted(Today).Should().BeFalse();
    }

    private static CareerEventInput Valid(CareerEventKind kind = CareerEventKind.IncomeStep) => new(
        kind, new DateOnly(2027, 3, 1), kind == CareerEventKind.Leave ? new DateOnly(2027, 5, 31) : null, "Promotion", 500m, "EUR", null);

    private static IEnumerable<string> Errors(CareerEventInput input)
        => new CareerEventInputValidator(Clock).Validate(input).Errors.Select(e => e.ErrorMessage);

    [Theory]
    [InlineData(CareerEventKind.IncomeStep)]
    [InlineData(CareerEventKind.OneOffIn)]
    [InlineData(CareerEventKind.OneOffOut)]
    [InlineData(CareerEventKind.Leave)]
    public void Validator_AcceptsAWellFormedEventOfEveryKind(CareerEventKind kind)
        => Errors(Valid(kind)).Should().BeEmpty();

    [Fact]
    public void Validator_RefusesMissingRequiredFields_InsteadOfBindingToDefaults()
    {
        var errors = Errors(new CareerEventInput(null, null, null, null, null, null, null));

        errors.Should().Contain(["Kind is required.", "Date is required.", "Label is required.", "Amount is required."]);
        errors.Should().Contain("Currency must be a known three-letter code.");
    }

    [Fact]
    public void Validator_AnIncomeStepMayBeNegative_ButNotZero()
    {
        Errors(Valid() with { Amount = -300m }).Should().BeEmpty("a pay cut is a negative step");
        Errors(Valid() with { Amount = 0m }).Should().Contain("An income step must change income by something.");
    }

    [Theory]
    [InlineData(CareerEventKind.OneOffIn)]
    [InlineData(CareerEventKind.OneOffOut)]
    [InlineData(CareerEventKind.Leave)]
    public void Validator_EveryOtherKindNeedsAPositiveAmount(CareerEventKind kind)
        => Errors(Valid(kind) with { Amount = -1m }).Should().Contain("Amount must be greater than zero.");

    [Fact]
    public void Validator_ALeaveNeedsAnEndDateOnOrAfterItsStart_AndNoOtherKindMayHaveOne()
    {
        Errors(Valid(CareerEventKind.Leave) with { EndDate = null }).Should().Contain("A leave needs an end date.");
        Errors(Valid(CareerEventKind.Leave) with { EndDate = new DateOnly(2027, 2, 1) }).Should().Contain("End date cannot be before the date.");
        Errors(Valid() with { EndDate = new DateOnly(2027, 5, 1) }).Should().Contain("Only a leave has an end date.");
    }

    [Fact]
    public void Validator_BoundsTheDateTheLabelTheNoteAndTheCurrency()
    {
        Errors(Valid() with { Date = Today.AddYears(CareerEventLimits.MaxYearsAhead + 1) })
            .Should().Contain($"Date can be at most {CareerEventLimits.MaxYearsAhead} years ahead.");
        Errors(Valid() with { Label = new string('x', CareerEventLimits.LabelMaxLength + 1) }).Should().NotBeEmpty();
        Errors(Valid() with { Note = new string('x', CareerEventLimits.NoteMaxLength + 1) }).Should().NotBeEmpty();
        Errors(Valid() with { Currency = "ZZZ" }).Should().Contain("Currency must be a known three-letter code.");
        Errors(Valid() with { Amount = CareerEventLimits.MaxAmount }).Should().Contain("Amount is too large.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

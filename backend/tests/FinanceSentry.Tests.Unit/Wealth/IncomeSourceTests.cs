namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Validators;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>A declared income (#454 S4): its monthly equivalent, when it runs, how it retires, and what it accepts. Synthetic figures.</summary>
public sealed class IncomeSourceTests
{
    private static readonly Guid UserId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static IncomeSource Source(
        IncomeCadence cadence = IncomeCadence.Monthly, decimal amount = 3_000m, DateOnly? start = null, DateOnly? end = null)
        => IncomeSource.Create(UserId, IncomeKind.Salary, " Day job ", amount, "eur", cadence, IncomeBasis.Net, start ?? new DateOnly(2025, 1, 1), end);

    [Fact]
    public void Create_TrimsTheLabel_AndUppercasesTheCurrency()
    {
        var source = Source();

        source.Label.Should().Be("Day job");
        source.Currency.Should().Be("EUR");
        source.UserId.Should().Be(UserId);
    }

    [Theory]
    [InlineData(IncomeCadence.Monthly, 3_000, 3_000)]
    [InlineData(IncomeCadence.Weekly, 600, 2_600)]
    [InlineData(IncomeCadence.Fortnightly, 1_200, 2_600)]
    [InlineData(IncomeCadence.Annual, 36_000, 3_000)]
    public void MonthlyEquivalent_ConvertsTheCadenceToAMonth(IncomeCadence cadence, double amount, double expected)
        => Source(cadence, (decimal)amount).MonthlyEquivalent.Should().Be((decimal)expected);

    [Fact]
    public void MonthlyEquivalent_IsNull_ForAnIrregularIncome()
        => Source(IncomeCadence.Irregular).MonthlyEquivalent.Should().BeNull("a guess would pass as a fact");

    [Fact]
    public void IsActiveOn_IsBoundedByTheStartAndTheEndDate()
    {
        var source = Source(start: new DateOnly(2026, 1, 1), end: new DateOnly(2026, 6, 30));

        source.IsActiveOn(new DateOnly(2025, 12, 31)).Should().BeFalse();
        source.IsActiveOn(new DateOnly(2026, 1, 1)).Should().BeTrue();
        source.IsActiveOn(new DateOnly(2026, 6, 30)).Should().BeTrue("the end date is the last day it is received");
        source.IsActiveOn(new DateOnly(2026, 7, 1)).Should().BeFalse();
        Source().IsActiveOn(Today).Should().BeTrue("no end date means it continues");
    }

    [Fact]
    public void Retire_EndsTheIncomeToday()
    {
        var source = Source();

        source.Retire(Today);

        source.EndDate.Should().Be(Today);
        source.IsActiveOn(Today.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Retire_LeavesAnEarlierEndDateAlone()
    {
        var source = Source(end: new DateOnly(2026, 3, 31));

        source.Retire(Today);

        source.EndDate.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void Retire_OfAnIncomeThatHasNotStarted_KeepsTheDatesValid_ButItNeverRuns()
    {
        var start = new DateOnly(2026, 12, 1);
        var source = Source(start: start);

        source.Retire(new DateOnly(2026, 11, 5));

        source.EndDate.Should().Be(start, "an end date before the start would be refused on the next edit");
        source.IsActiveOn(start).Should().BeFalse("it was retired before it ever started");
    }

    [Fact]
    public void Retire_OnTheStartDate_StillRunsThatDay()
    {
        var source = Source(start: Today);

        source.Retire(Today);

        source.IsActiveOn(Today).Should().BeTrue();
        source.IsActiveOn(Today.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Update_OfARetiredBeforeStartSource_RestatesTheDatesAndRunsAgain()
    {
        var start = new DateOnly(2026, 12, 1);
        var source = Source(start: start);
        source.Retire(new DateOnly(2026, 11, 5));

        source.Update(IncomeKind.Salary, "Day job", 3_000m, "EUR", IncomeCadence.Monthly, IncomeBasis.Net, start, null);

        source.RetiredAt.Should().BeNull();
        source.IsActiveOn(start).Should().BeTrue();
    }
}

public sealed class IncomeSourceInputValidatorTests
{
    private static IncomeSourceInput Valid() => new(
        IncomeKind.Salary, "Day job", 3_000m, "EUR", IncomeCadence.Monthly, IncomeBasis.Net, new DateOnly(2025, 1, 1), null);

    private static bool IsValid(IncomeSourceInput input) => new IncomeSourceInputValidator().Validate(input).IsValid;

    [Fact]
    public void AFullInput_IsValid() => IsValid(Valid()).Should().BeTrue();

    public static TheoryData<IncomeSourceInput> Invalid() => new()
    {
        Valid() with { Kind = null },
        Valid() with { Kind = (IncomeKind)99 },
        Valid() with { Label = " " },
        Valid() with { Label = new string('x', IncomeSourceLimits.LabelMaxLength + 1) },
        Valid() with { Amount = null },
        Valid() with { Amount = 0m },
        Valid() with { Amount = IncomeSourceLimits.MaxAmount },
        Valid() with { Currency = "ZZZ" },
        Valid() with { Currency = "EU" },
        Valid() with { Cadence = null },
        Valid() with { Cadence = (IncomeCadence)99 },
        Valid() with { GrossOrNet = null },
        Valid() with { StartDate = null },
        Valid() with { EndDate = new DateOnly(2024, 12, 31) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnInvalidInput_IsRefused(IncomeSourceInput input) => IsValid(input).Should().BeFalse();

    [Fact]
    public void AnEndDateOnTheStartDate_IsValid()
        => IsValid(Valid() with { EndDate = new DateOnly(2025, 1, 1) }).Should().BeTrue();
}

namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Tests.Unit.Core.Measures.MeasureFixture;

/// <summary>
/// The XIRR port against values computed elsewhere. "Spreadsheet" means LibreOffice Calc 7.4.7.2's XIRR on the
/// same cash flows and dates, evaluated headless from a sheet; its value for the Microsoft example matches the
/// published one, which is what makes it a fair oracle for the rest.
/// </summary>
public class XirrTests
{
    // XIRR is solved to the original's precision (1e-6 on the step and on the NPV), so it is compared at 1e-6.
    private const double RateTolerance = 0.000001;

    [Fact]
    public void Solve_MicrosoftDocsExample_MatchesPublishedRate()
    {
        // Oracle: Microsoft's XIRR documentation example, published result 0.373362535 (37.34%);
        // spreadsheet 0.373362533518832.
        var outcome = Xirr.Solve(
        [
            new(Day(2008, 1, 1), -10000d),
            new(Day(2008, 3, 1), 2750d),
            new(Day(2008, 10, 30), 4250d),
            new(Day(2009, 2, 15), 3250d),
            new(Day(2009, 4, 1), 2750d),
        ]);

        outcome.Failure.Should().BeNull();
        outcome.Rate!.Value.Should().BeApproximately(0.373362533518832, RateTolerance);
    }

    [Fact]
    public void Solve_DeepLossOverEightMonths_ConvergesNearMinusOneHundredPercent()
    {
        // Oracle: ExcelFinancialFunctions' own regression test XirrIssue27 (-0.990247691899517);
        // spreadsheet -0.990247691899517. The root has the other sign from the default guess.
        var outcome = Xirr.Solve([new(Day(2020, 7, 3), -177900000d), new(Day(2021, 2, 25), 8799805.85d)]);

        outcome.Rate!.Value.Should().BeApproximately(-0.990247691899517, RateTolerance);
    }

    [Fact]
    public void Solve_AllCashFlowsPaidIn_FailsNoSignChange()
    {
        // Oracle: no rate discounts flows of one sign to zero; spreadsheet Err:502 (invalid argument).
        var outcome = Xirr.Solve([new(Day(2026, 1, 1), -1000d), new(Day(2027, 1, 1), -50d)]);

        outcome.Should().Be(new XirrOutcome(null, MeasureFailure.NoSignChange));
    }

    [Fact]
    public void Solve_AllCashFlowsTakenOut_FailsNoSignChange()
    {
        var outcome = Xirr.Solve([new(Day(2026, 1, 1), 1000d), new(Day(2027, 1, 1), 50d)]);

        outcome.Should().Be(new XirrOutcome(null, MeasureFailure.NoSignChange));
    }

    [Fact]
    public void Solve_NoRealRoot_FailsNoConvergence()
    {
        // Oracle: hand. With y = 1 + r, -100 + 200/y - 101/y^2 = 0 is -100y^2 + 200y - 101 = 0, whose
        // discriminant 200^2 - 4·100·101 = -400 is negative: there is no rate. Spreadsheet Err:502.
        var outcome = Xirr.Solve(
            [new(Day(2026, 1, 1), -100d), new(Day(2027, 1, 1), 200d), new(Day(2028, 1, 1), -101d)]);

        outcome.Should().Be(new XirrOutcome(null, MeasureFailure.NoConvergence));
    }

    [Fact]
    public void Solve_DateBeforeTheFirst_FailsInvalidInput()
    {
        var outcome = Xirr.Solve([new(Day(2027, 1, 1), -100d), new(Day(2026, 1, 1), 110d)]);

        outcome.Should().Be(new XirrOutcome(null, MeasureFailure.InvalidInput));
    }

    [Fact]
    public void Solve_NoCashFlows_FailsInvalidInput()
        => Xirr.Solve([]).Should().Be(new XirrOutcome(null, MeasureFailure.InvalidInput));
}

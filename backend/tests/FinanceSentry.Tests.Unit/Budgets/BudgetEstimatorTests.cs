namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Estimates;
using Xunit;

/// <summary>The estimator: median over the usable months, commitments over committed lines, month-end expectation. Synthetic figures only.</summary>
public sealed class BudgetEstimatorTests
{
    private static readonly DateOnly Month = new(2027, 4, 1);

    private static readonly string[] SixMonths = ["2026-10", "2026-11", "2026-12", "2027-01", "2027-02", "2027-03"];

    private static EstimateHistory History(params string[] months) => new(months, []);

    private static CategoryMonthSpend Spend(string month, string line, decimal usd) => new(month, line, usd);

    private static LineSuggestion Suggest(
        string line, EstimateHistory history, CategoryMonthSpend[] rows, CommitmentOnLine[]? commitments = null)
        => BudgetEstimator.Suggest(history, rows, commitments ?? [], Month)[line];

    [Fact]
    public void Median_OfSixMonths_AveragesTheTwoMiddleOnes()
    {
        var rows = SixMonths
            .Zip(new[] { 100m, 120m, 140m, 160m, 180m, 200m }, (m, v) => Spend(m, "FOOD_AND_DRINK", v))
            .ToArray();

        var suggestion = Suggest("FOOD_AND_DRINK", History(SixMonths), rows);

        Assert.Equal(150m, suggestion.Amount);
        Assert.Equal(EstimateBasis.History, suggestion.Basis);
    }

    [Fact]
    public void Median_IgnoresOneOffMonths_WhereAMeanWouldNot()
    {
        // Five ordinary months and one spike: the mean is 90, the median stays at the typical month.
        var rows = SixMonths
            .Zip(new[] { 50m, 50m, 50m, 50m, 50m, 290m }, (m, v) => Spend(m, "ENTERTAINMENT", v))
            .ToArray();

        Assert.Equal(50m, Suggest("ENTERTAINMENT", History(SixMonths), rows).Amount);
    }

    [Fact]
    public void AMonthWithNoSpendInTheLine_CountsAsZero()
    {
        // Spend in 2 of 5 usable months: the median over five values (0, 0, 0, 40, 60) is 0, not 50.
        var months = SixMonths[..5];
        var rows = new[] { Spend("2026-10", "TRAVEL", 40m), Spend("2026-11", "TRAVEL", 60m) };

        Assert.Equal(0m, Suggest("TRAVEL", History(months), rows).Amount);
    }

    [Fact]
    public void SpendInAMonthOutsideTheWindow_DoesNotCount()
    {
        // 2026-09 is not a usable month (skipped upstream): its 5000 must not reach the median.
        var months = SixMonths[..3];
        var rows = new[]
        {
            Spend("2026-09", "FOOD_AND_DRINK", 5000m),
            Spend("2026-10", "FOOD_AND_DRINK", 100m),
            Spend("2026-11", "FOOD_AND_DRINK", 110m),
            Spend("2026-12", "FOOD_AND_DRINK", 120m),
        };

        Assert.Equal(110m, Suggest("FOOD_AND_DRINK", History(months), rows).Amount);
    }

    [Fact]
    public void BelowThreeUsableMonths_ThereIsNoHistorySuggestion()
    {
        var rows = new[] { Spend("2026-10", "FOOD_AND_DRINK", 100m), Spend("2026-11", "FOOD_AND_DRINK", 100m) };

        var suggestion = Suggest("FOOD_AND_DRINK", History("2026-10", "2026-11"), rows);

        Assert.Null(suggestion.Amount);
        Assert.Null(suggestion.Basis);
    }

    [Fact]
    public void LinesThatAreNotSpending_NeverGetASuggestion()
    {
        var rows = SixMonths.SelectMany(m => new[]
        {
            Spend(m, "INCOME", 3000m),
            Spend(m, "TRANSFER_OUT", 800m),
            Spend(m, "FOOD_AND_DRINK", 100m),
        }).ToArray();

        var suggestions = BudgetEstimator.Suggest(History(SixMonths), rows, [], Month);

        Assert.Equal(["FOOD_AND_DRINK"], suggestions.Keys);
    }

    [Fact]
    public void CommittedLine_UsesTheCommitmentInsteadOfTheMedian()
    {
        // Five installments finished, one remains: the median still pays the old total, the commitment does not.
        var rows = SixMonths.Select(m => Spend(m, "LOAN_PAYMENTS", 226m)).ToArray();
        var commitments = new[] { new CommitmentOnLine("LOAN_PAYMENTS", 182m, null) };

        var suggestion = Suggest("LOAN_PAYMENTS", History(SixMonths), rows, commitments);

        Assert.Equal(182m, suggestion.Amount);
        Assert.Equal(EstimateBasis.Commitment, suggestion.Basis);
    }

    [Fact]
    public void CommittedLine_WithSeveralCommitments_SumsThem()
    {
        var rows = SixMonths.Select(m => Spend(m, "LOAN_PAYMENTS", 500m)).ToArray();
        var commitments = new[]
        {
            new CommitmentOnLine("LOAN_PAYMENTS", 100m, null),
            new CommitmentOnLine("LOAN_PAYMENTS", 60m, null),
        };

        Assert.Equal(160m, Suggest("LOAN_PAYMENTS", History(SixMonths), rows, commitments).Amount);
    }

    [Fact]
    public void CommittedLine_WhoseCommitmentEndedBeforeTheMonth_FallsBackToTheMedian()
    {
        var rows = SixMonths.Select(m => Spend(m, "RENT_AND_UTILITIES", 300m)).ToArray();
        var ended = new[] { new CommitmentOnLine("RENT_AND_UTILITIES", 900m, new DateOnly(2027, 3, 1)) };

        var suggestion = Suggest("RENT_AND_UTILITIES", History(SixMonths), rows, ended);

        Assert.Equal(300m, suggestion.Amount);
        Assert.Equal(EstimateBasis.History, suggestion.Basis);
    }

    [Fact]
    public void Commitment_WhoseFinalPaymentIsThisMonth_StillCounts()
    {
        var rows = SixMonths.Select(m => Spend(m, "LOAN_PAYMENTS", 300m)).ToArray();
        var lastMonth = new[] { new CommitmentOnLine("LOAN_PAYMENTS", 90m, Month) };

        Assert.Equal(90m, Suggest("LOAN_PAYMENTS", History(SixMonths), rows, lastMonth).Amount);
    }

    [Fact]
    public void CommittedLine_WithACommitmentButNoHistory_StillGetsTheCommitment()
    {
        var commitments = new[] { new CommitmentOnLine("LOAN_PAYMENTS", 182m, null) };

        var suggestion = Suggest("LOAN_PAYMENTS", History("2027-02"), [], commitments);

        Assert.Equal(182m, suggestion.Amount);
        Assert.Equal(EstimateBasis.Commitment, suggestion.Basis);
    }

    [Fact]
    public void DiscretionaryLine_TakesTheCommitmentAsAFloor_NotAReplacement()
    {
        // A subscription of 9 beside 65 of ordinary entertainment: the median stays.
        var rows = SixMonths.Select(m => Spend(m, "ENTERTAINMENT", 65m)).ToArray();
        var commitments = new[] { new CommitmentOnLine("ENTERTAINMENT", 9m, null) };

        var suggestion = Suggest("ENTERTAINMENT", History(SixMonths), rows, commitments);

        Assert.Equal(65m, suggestion.Amount);
        Assert.Equal(EstimateBasis.History, suggestion.Basis);
    }

    [Fact]
    public void DiscretionaryLine_NeverEstimatesBelowWhatItsCommitmentsCharge()
    {
        var rows = SixMonths.Select(m => Spend(m, "ENTERTAINMENT", 4m)).ToArray();
        var commitments = new[] { new CommitmentOnLine("ENTERTAINMENT", 9m, null) };

        var suggestion = Suggest("ENTERTAINMENT", History(SixMonths), rows, commitments);

        Assert.Equal(9m, suggestion.Amount);
        Assert.Equal(EstimateBasis.Commitment, suggestion.Basis);
    }

    [Fact]
    public void ALineWithOnlyACommitment_AppearsWithoutAnyHistory()
    {
        var commitments = new[] { new CommitmentOnLine("HOUSEHOLD", 300m, null) };

        var suggestions = BudgetEstimator.Suggest(History(SixMonths), [], commitments, Month);

        Assert.Equal(300m, suggestions["HOUSEHOLD"].Amount);
    }

    // -- month-end expectation --------------------------------------------------------------------------------

    private static readonly DateTime Day3 = new(2027, 4, 3);
    private static readonly DateTime Day14 = new(2027, 4, 14);

    [Fact]
    public void ExpectedMonthEnd_OfAPastMonth_IsWhatWasSpent()
        => Assert.Equal(70m, BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", 100m, 70m, MonthPhase.Past, Day14));

    [Fact]
    public void ExpectedMonthEnd_OfAFutureMonth_IsTheAmount()
        => Assert.Equal(100m, BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", 100m, 0m, MonthPhase.Future, Day14));

    [Fact]
    public void ExpectedMonthEnd_WithNoAmount_IsWhatWasSpent()
        => Assert.Equal(35m, BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", null, 35m, MonthPhase.Current, Day14));

    [Fact]
    public void ExpectedMonthEnd_BeforeDaySeven_IsSpentPlusWhatIsStillDue()
        => Assert.Equal(100m, BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", 100m, 40m, MonthPhase.Current, Day3));

    [Fact]
    public void ExpectedMonthEnd_BeforeDaySeven_NeverFallsBelowWhatWasSpent()
        => Assert.Equal(130m, BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", 100m, 130m, MonthPhase.Current, Day3));

    [Fact]
    public void ExpectedMonthEnd_OnDay14_ProjectsAPaceThatBeatsTheAmount()
    {
        // 80 spent by day 14 of 30 projects to 171.4, over the 100 estimate.
        var expected = BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", 100m, 80m, MonthPhase.Current, Day14);

        Assert.Equal(171.4m, Math.Round(expected, 1));
    }

    [Fact]
    public void ExpectedMonthEnd_OnDay14_WithAPaceUnderTheAmount_IsTheAmount()
    {
        // 30 spent by day 14 projects to 64.3, under the 100 estimate: still 100 expected.
        Assert.Equal(100m, BudgetEstimator.ExpectedMonthEnd("FOOD_AND_DRINK", 100m, 30m, MonthPhase.Current, Day14));
    }

    [Fact]
    public void ExpectedMonthEnd_OfACommittedLine_NeverProjectsAPace()
    {
        // A rent of 900 paid on the 1st reads day 14 as 900 / (14/30) = 1928 if paced; it is 900, already paid.
        Assert.Equal(900m, BudgetEstimator.ExpectedMonthEnd("RENT_AND_UTILITIES", 900m, 900m, MonthPhase.Current, Day14));
    }

    [Fact]
    public void ExpectedMonthEnd_OfACommittedLine_ThatIsNotYetPaid_AddsWhatIsDue()
        => Assert.Equal(900m, BudgetEstimator.ExpectedMonthEnd("RENT_AND_UTILITIES", 900m, 0m, MonthPhase.Current, Day14));
}

namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Estimates;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The month's estimate end to end over a mocked spend reader: the exclusion window, the coverage-hole rule, the
/// hand-edit override and its reset. Synthetic figures only.
/// </summary>
public sealed class BudgetEstimateServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateOnly FirstData = new(2026, 1, 1);

    private readonly Mock<IMonthlyCategorySpendReader> _spend = new();
    private readonly Mock<ICoverageHoleReader> _holes = new();
    private readonly List<CategoryMonthSpend> _rows = [];
    private readonly List<CommitmentOnLine> _commitments = [];
    private readonly List<CommitmentOnLine> _endedCommitments = [];
    private readonly List<CoverageHoleSpan> _holeSpans = [];
    private BudgetEstimateOptions _options = new();
    private DateOnly? _firstDataDay = FirstData;

    public BudgetEstimateServiceTests()
    {
        _spend.Setup(s => s.GetAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MonthlyCategorySpend(_firstDataDay, [.. _rows], [.. _commitments], [.. _endedCommitments]));
        _holes.Setup(h => h.GetHolesAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. _holeSpans]);
    }

    private BudgetEstimateService Service() =>
        new(_spend.Object, _holes.Object, new CategoryNormalizationService(), Options.Create(_options));

    private Task<FinanceSentry.Modules.Budgets.API.Responses.BudgetSummaryResponse> Month(
        int year, int month, int day = 15, params Budget[] overrides)
        => Service().ComputeAsync(UserId, year, month, new DateTime(year, month, day), overrides);

    private Task<FinanceSentry.Modules.Budgets.API.Responses.BudgetSummaryResponse> Now(
        DateTime asOf, params Budget[] overrides)
        => Service().ComputeAsync(UserId, asOf.Year, asOf.Month, asOf, overrides);

    private void Spend(string month, string line, decimal usd) => _rows.Add(new CategoryMonthSpend(month, line, usd));

    private void SpendEveryMonth(string line, decimal usd, params string[] months)
    {
        foreach (var month in months)
            Spend(month, line, usd);
    }

    private static Budget Override(string category, decimal amount) => Budget.Create(UserId, category, amount, "USD");

    // -- the estimator, through the service -----------------------------------------------------------------

    [Fact]
    public async Task Estimate_IsTheMedianOfTheUsableMonths()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        string[] months = ["2026-09", "2026-10", "2026-11", "2026-12", "2027-01", "2027-02"];
        foreach (var (m, v) in months.Zip<string, decimal>([300m, 310m, 320m, 330m, 340m, 900m]))
            Spend(m, "FOOD_AND_DRINK", v);

        var summary = await Month(2027, 3);

        var line = Assert.Single(summary.Items);
        Assert.Equal("FOOD_AND_DRINK", line.Category);
        Assert.Equal(325m, line.Estimate);
        Assert.Equal(325m, line.SuggestedAmount);
        Assert.Equal("history", line.Source);
        Assert.Null(line.OverrideId);
        Assert.True(summary.HasEnoughHistory);
        Assert.Equal(6, summary.HistoryMonthsUsed);
        Assert.Equal(325m, summary.TotalEstimate);
    }

    [Fact]
    public async Task LessThanThreeUsableMonths_SuggestsNothing_AndSaysHowMuchHistoryItHas()
    {
        _firstDataDay = new DateOnly(2027, 1, 1);
        SpendEveryMonth("FOOD_AND_DRINK", 200m, "2027-01", "2027-02");

        var summary = await Month(2027, 3);

        Assert.False(summary.HasEnoughHistory);
        Assert.Equal(2, summary.HistoryMonthsUsed);
        Assert.Equal(3, summary.MinHistoryMonths);
        Assert.Empty(summary.Items);
    }

    // -- the exclusion window (September and October 2026) --------------------------------------------------

    [Fact]
    public async Task ExclusionWindow_SeptemberAndOctober2026_NeverReachTheEstimate()
    {
        // The card moved off the bank link in the autumn: food reads as nearly nothing in those two months.
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-05", "2026-06", "2026-07", "2026-08");
        SpendEveryMonth("FOOD_AND_DRINK", 5m, "2026-09", "2026-10");

        var summary = await Month(2026, 11);

        var line = Assert.Single(summary.Items);
        Assert.Equal(400m, line.Estimate);
        Assert.Equal(4, summary.HistoryMonthsUsed);
        Assert.Equal(
            ["2026-09", "2026-10"], summary.SkippedMonths.Select(s => s.Month));
        Assert.All(summary.SkippedMonths, s => Assert.Equal(SkipReasons.Excluded, s.Reason));
    }

    [Fact]
    public async Task ExclusionWindow_ExcludedMonthsAreSkippedNotZero_SoTheMedianIsNotDraggedDown()
    {
        // Counted as zeros, 4 real months (400) and 2 empty ones would give a median of 400 only by luck; with
        // three real months and two skipped ones the zeros would win. The skipped months must not count at all.
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-06", "2026-07", "2026-08");

        var summary = await Month(2026, 11);

        Assert.Equal(400m, Assert.Single(summary.Items).Estimate);
    }

    [Fact]
    public async Task ExclusionWindow_LeavesTooLittleHistory_SoNoEstimateIsInvented()
    {
        _firstDataDay = new DateOnly(2026, 7, 1);
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-07", "2026-08");
        SpendEveryMonth("FOOD_AND_DRINK", 5m, "2026-09", "2026-10");

        var summary = await Month(2026, 11);

        Assert.False(summary.HasEnoughHistory);
        Assert.Equal(2, summary.HistoryMonthsUsed);
        Assert.Equal(2, summary.SkippedMonths.Count);
    }

    [Fact]
    public async Task ExclusionWindow_CanBeLifted_ByConfiguration()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-05", "2026-06", "2026-07", "2026-08");
        SpendEveryMonth("FOOD_AND_DRINK", 100m, "2026-09", "2026-10");

        var summary = await Month(2026, 11);

        Assert.Equal(400m, Assert.Single(summary.Items).Estimate);
        Assert.Equal(6, summary.HistoryMonthsUsed);
        Assert.Empty(summary.SkippedMonths);
    }

    [Fact]
    public async Task ExclusionWindow_StillShowsWhatIsSpentInThoseMonthsWhenTheyAreViewed()
    {
        // Viewing October itself: its spend is shown as spend; only the learning skips it.
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-04", "2026-05", "2026-06", "2026-07", "2026-08");
        Spend("2026-10", "FOOD_AND_DRINK", 5m);

        var summary = await Month(2026, 10);

        var line = Assert.Single(summary.Items);
        Assert.Equal(5m, line.Spent);
        Assert.Equal(400m, line.Estimate);
    }

    // -- the coverage-hole rule -----------------------------------------------------------------------------

    [Fact]
    public async Task CoverageHole_MonthIsSkippedNotZero_SoAQuietPeriodIsNotReadAsFrugal()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2027-01", "2027-02", "2027-04", "2027-05");
        // March is quiet because of the hole: counted as a zero it would drag the median down.
        _holeSpans.Add(new CoverageHoleSpan(Account, new DateOnly(2027, 3, 8), new DateOnly(2027, 3, 28)));

        var summary = await Month(2027, 6);

        Assert.Equal(400m, Assert.Single(summary.Items).Estimate);
        var skipped = Assert.Single(summary.SkippedMonths);
        Assert.Equal("2027-03", skipped.Month);
        Assert.Equal(SkipReasons.CoverageGap, skipped.Reason);
        Assert.Equal(5, summary.HistoryMonthsUsed);
    }

    [Fact]
    public async Task CoverageHole_OpenHoleSkipsEveryLaterMonth()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-12", "2027-01", "2027-02", "2027-03");
        _holeSpans.Add(new CoverageHoleSpan(Account, new DateOnly(2027, 4, 5), null));

        var summary = await Month(2027, 6);

        Assert.Equal(["2027-04", "2027-05"], summary.SkippedMonths.Select(s => s.Month));
        Assert.Equal(400m, Assert.Single(summary.Items).Estimate);
    }

    [Fact]
    public async Task CoverageHole_AMonthOfRealZeroSpendOutsideAnyHoleStillCountsAsZero()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        // Travel in 1 of 6 months, no hole: the median is 0.
        Spend("2027-01", "TRAVEL", 900m);
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");

        var summary = await Month(2027, 6);

        Assert.DoesNotContain(summary.Items, i => i.Category == "TRAVEL" && i.Estimate > 0m);
    }

    [Fact]
    public async Task MonthsBeforeTheFirstData_AreNoData_NotZero()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        _firstDataDay = new DateOnly(2027, 2, 1);
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2027-02", "2027-03", "2027-04");

        var summary = await Month(2027, 5);

        Assert.Equal(400m, Assert.Single(summary.Items).Estimate);
        Assert.Equal(3, summary.HistoryMonthsUsed);
        Assert.Empty(summary.SkippedMonths);
    }

    // -- hand edits are overrides ---------------------------------------------------------------------------

    private void SixMonthsOfFood(decimal usd)
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("FOOD_AND_DRINK", usd, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");
    }

    [Fact]
    public async Task Override_ReplacesTheSuggestion_AndKeepsItToResetTo()
    {
        SixMonthsOfFood(400m);
        var edit = Override("FOOD_AND_DRINK", 250m);

        var summary = await Month(2027, 6, 15, edit);

        var line = Assert.Single(summary.Items);
        Assert.Equal(250m, line.Estimate);
        Assert.Equal(400m, line.SuggestedAmount);
        Assert.Equal("override", line.Source);
        Assert.Equal(edit.Id, line.OverrideId);
        Assert.Equal(250m, summary.TotalEstimate);
    }

    [Fact]
    public async Task Override_Removed_ReturnsTheLineToItsSuggestion()
    {
        SixMonthsOfFood(400m);

        var summary = await Month(2027, 6);

        var line = Assert.Single(summary.Items);
        Assert.Equal(400m, line.Estimate);
        Assert.Equal("history", line.Source);
        Assert.Null(line.OverrideId);
    }

    [Fact]
    public async Task Override_OnALineWithNoHistory_StillShows_WithNoSuggestion()
    {
        SixMonthsOfFood(400m);
        var edit = Override("TRAVEL", 600m);

        var summary = await Month(2027, 6, 15, edit);

        var travel = summary.Items.Single(i => i.Category == "TRAVEL");
        Assert.Equal(600m, travel.Estimate);
        Assert.Null(travel.SuggestedAmount);
        Assert.Equal("override", travel.Source);
    }

    [Fact]
    public async Task Override_WithoutEnoughHistory_StillStandsAsTheLinesAmount()
    {
        _firstDataDay = new DateOnly(2027, 5, 1);
        var edit = Override("FOOD_AND_DRINK", 300m);

        var summary = await Month(2027, 6, 15, edit);

        Assert.False(summary.HasEnoughHistory);
        Assert.Equal(300m, Assert.Single(summary.Items).Estimate);
    }

    [Fact]
    public async Task Override_InAnotherCurrency_IsConvertedToUsd()
    {
        SixMonthsOfFood(400m);
        var edit = Budget.Create(UserId, "FOOD_AND_DRINK", 4000m, "UAH");

        var summary = await Month(2027, 6, 15, edit);

        // 4000 UAH at the fallback rate of 0.024 is 96 USD.
        Assert.Equal(96m, Assert.Single(summary.Items).Estimate);
    }

    [Fact]
    public async Task Override_IsAppliedToThePastMonthsToo_ButTheSuggestionStaysThatMonthsOwn()
    {
        SixMonthsOfFood(400m);
        var edit = Override("FOOD_AND_DRINK", 250m);

        // March read from June: its own window (September to February) holds three months of food and three empty
        // ones, so its own suggestion is 200; the hand edit still stands over it.
        var summary = await Service().ComputeAsync(UserId, 2027, 3, new DateTime(2027, 6, 15), [edit]);

        var line = Assert.Single(summary.Items);
        Assert.Equal(250m, line.Estimate);
        Assert.Equal(200m, line.SuggestedAmount);
    }

    // -- spend, lines, commitments --------------------------------------------------------------------------

    [Fact]
    public async Task ALineWithSpendButNoEstimate_ShowsAsSpendOnly()
    {
        SixMonthsOfFood(400m);
        Spend("2027-06", "MEDICAL", 75m);

        var summary = await Month(2027, 6);

        var medical = summary.Items.Single(i => i.Category == "MEDICAL");
        Assert.Null(medical.Estimate);
        Assert.Equal("none", medical.Source);
        Assert.Equal(75m, medical.Spent);
        Assert.False(medical.IsOverBudget);
    }

    [Fact]
    public async Task ALineWithAMedianOfZeroAndNoSpend_IsHidden()
    {
        SixMonthsOfFood(400m);
        Spend("2027-01", "TRAVEL", 900m);

        var summary = await Month(2027, 6);

        Assert.DoesNotContain(summary.Items, i => i.Category == "TRAVEL");
    }

    [Fact]
    public async Task Commitment_ReplacesTheMedianOnACommittedLine_ForTheCurrentMonth()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("LOAN_PAYMENTS", 226m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");
        _commitments.Add(new CommitmentOnLine("LOAN_PAYMENTS", 182m, null));

        var summary = await Month(2027, 6);

        var line = Assert.Single(summary.Items);
        Assert.Equal(182m, line.Estimate);
        Assert.Equal("commitment", line.Source);
    }

    [Fact]
    public async Task CommittedLine_WhoseInstallmentWasPaidOff_GetsNoSuggestion_NotTheMedianOfTheFinishedPayments()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("LOAN_PAYMENTS", 200m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");
        _endedCommitments.Add(new CommitmentOnLine("LOAN_PAYMENTS", 200m, new DateOnly(2027, 5, 1)));

        var summary = await Month(2027, 6);

        Assert.Empty(summary.Items);
        Assert.Equal(0m, summary.TotalEstimate);
        Assert.Equal(0m, summary.ExpectedMonthEnd);
    }

    [Fact]
    public async Task CommittedLine_WhoseRentStopped_GetsNoSuggestion()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("RENT_AND_UTILITIES", 900m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");
        _endedCommitments.Add(new CommitmentOnLine("RENT_AND_UTILITIES", 900m, null));

        var summary = await Month(2027, 6);

        Assert.Empty(summary.Items);
        Assert.Equal(0m, summary.TotalEstimate);
    }

    [Fact]
    public async Task CommittedLine_ThatNeverHadACommitment_KeepsTheMedian()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("RENT_AND_UTILITIES", 900m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");

        var summary = await Month(2027, 6);

        var line = Assert.Single(summary.Items);
        Assert.Equal(900m, line.Estimate);
        Assert.Equal("history", line.Source);
    }

    [Fact]
    public async Task EndedCommitment_DoesNotSuppressThePastMonthsOwnMedian()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("LOAN_PAYMENTS", 200m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");
        _endedCommitments.Add(new CommitmentOnLine("LOAN_PAYMENTS", 200m, new DateOnly(2027, 5, 1)));

        var summary = await Service().ComputeAsync(UserId, 2027, 4, new DateTime(2027, 6, 15), []);

        Assert.Equal(200m, Assert.Single(summary.Items).Estimate);
    }

    [Fact]
    public async Task Commitment_DoesNotRewriteAPastMonth()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("LOAN_PAYMENTS", 226m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");
        _commitments.Add(new CommitmentOnLine("LOAN_PAYMENTS", 182m, null));

        var summary = await Service().ComputeAsync(UserId, 2027, 4, new DateTime(2027, 6, 15), []);

        Assert.Equal(226m, Assert.Single(summary.Items).Estimate);
    }

    [Fact]
    public async Task MonthEnd_IsSpentPlusWhatIsStillDue_AcrossLines()
    {
        SixMonthsOfFood(400m);
        _commitments.Add(new CommitmentOnLine("LOAN_PAYMENTS", 180m, null));
        Spend("2027-06", "FOOD_AND_DRINK", 100m);

        var summary = await Now(new DateTime(2027, 6, 3));

        // Food: 100 spent + 300 still due; loan: nothing paid yet, 180 due.
        Assert.Equal(100m, summary.TotalSpent);
        Assert.Equal(580m, summary.TotalEstimate);
        Assert.Equal(580m, summary.ExpectedMonthEnd);
    }

    [Fact]
    public async Task MonthEnd_OfAPastMonth_IsWhatWasSpent()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("FOOD_AND_DRINK", 400m, "2027-01", "2027-02", "2027-03");
        Spend("2027-04", "FOOD_AND_DRINK", 380m);

        var summary = await Service().ComputeAsync(UserId, 2027, 4, new DateTime(2027, 6, 15), []);

        Assert.Equal(380m, summary.ExpectedMonthEnd);
    }

    [Fact]
    public async Task Spend_ThatIsNotSpending_NeverBecomesALine()
    {
        SixMonthsOfFood(400m);
        Spend("2027-06", "INCOME", 3000m);
        Spend("2027-06", "TRANSFER_OUT", 800m);

        var summary = await Month(2027, 6);

        Assert.DoesNotContain(summary.Items, i => i.Category is "INCOME" or "TRANSFER_OUT");
        Assert.Equal(0m, summary.TotalSpent);
    }

    [Fact]
    public async Task HouseholdLine_IsALineOfItsOwn_WithAReadableLabel()
    {
        _options = new BudgetEstimateOptions { ExcludedMonths = [] };
        SpendEveryMonth("HOUSEHOLD", 330m, "2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05");

        var summary = await Month(2027, 6);

        var line = Assert.Single(summary.Items);
        Assert.Equal("HOUSEHOLD", line.Category);
        Assert.Equal("Household bills", line.CategoryLabel);
        Assert.Equal(330m, line.Estimate);
    }
}

namespace FinanceSentry.Tests.Unit.BankSync.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using Moq;
using Xunit;

/// <summary>
/// <see cref="MonthlyCategorySpendReader"/>: the Spending bucket of the flow breakdown, by month and line, plus the
/// commitments placed on the line their charges land in. Synthetic transactions only.
/// </summary>
public sealed class MonthlyCategorySpendReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private readonly Mock<IFlowBreakdownService> _breakdown = new();
    private readonly Mock<IActiveCommitmentsReader> _commitments = new();
    private readonly List<FlowBreakdownItem> _items = [];

    public MonthlyCategorySpendReaderTests()
    {
        _breakdown
            .Setup(b => b.GetRangeBreakdownUnscopedAsync(
                UserId, DateOnly.MinValue, It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new FlowBreakdown(string.Empty, [.. _items]));
        _commitments
            .Setup(c => c.GetActiveCommitmentsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _commitments
            .Setup(c => c.GetEndedCommitmentsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private MonthlyCategorySpendReader Reader() =>
        new(_breakdown.Object, _commitments.Object, FixedDetectionPhraseProvider.Seed);

    private void Add(
        DateTime date, decimal amountUsd, string bucket = FlowBuckets.Spending, string? category = "FOOD_AND_DRINK",
        string? flowRole = null, string description = "shop", string? merchant = "Corner Shop", string currency = "USD",
        decimal? native = null)
        => _items.Add(new FlowBreakdownItem(
            Guid.NewGuid(), Account, "Bank", "1234", currency, native ?? amountUsd, amountUsd, date, description,
            merchant, category, bucket == FlowBuckets.Income ? "in" : "out", bucket, null, flowRole));

    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly From = new(2027, 1, 1);
    private static readonly DateOnly To = new(2027, 3, 31);

    [Fact]
    public async Task SumsTheSpendingBucket_ByMonthAndCategory()
    {
        Add(Day(2027, 1, 5), 10m);
        Add(Day(2027, 1, 20), 15m);
        Add(Day(2027, 2, 3), 40m);
        Add(Day(2027, 2, 9), 7m, category: "TRAVEL");

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal(
            [("2027-01", "FOOD_AND_DRINK", 25m), ("2027-02", "FOOD_AND_DRINK", 40m), ("2027-02", "TRAVEL", 7m)],
            result.Months.Select(m => (m.Month, m.Line, m.SpendUsd)));
    }

    [Theory]
    [InlineData(FlowBuckets.Income)]
    [InlineData(FlowBuckets.Invested)]
    [InlineData(FlowBuckets.InvestmentReturn)]
    [InlineData(FlowBuckets.ExcludedPair)]
    [InlineData(FlowBuckets.ExcludedRouting)]
    [InlineData(FlowBuckets.ExcludedTransfer)]
    public async Task OnlyTheSpendingBucketIsSpend_TransfersRoutingAndInvestingAreNot(string bucket)
    {
        Add(Day(2027, 1, 5), 500m, bucket);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Empty(result.Months);
    }

    [Fact]
    public async Task FamilySupportAndHouseholdRoles_AreLinesOfTheirOwn()
    {
        Add(Day(2027, 1, 5), 300m, category: "TRANSFER_OUT", flowRole: FlowRoles.FamilySupport);
        Add(Day(2027, 1, 6), 120m, category: "LOAN_PAYMENTS", flowRole: FlowRoles.Household);
        Add(Day(2027, 1, 7), 20m);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal(
            [("FAMILY_SUPPORT", 300m), ("FOOD_AND_DRINK", 20m), ("HOUSEHOLD", 120m)],
            result.Months.Select(m => (m.Line, m.SpendUsd)));
    }

    [Fact]
    public async Task ASpendingRowWithAnIncomeOrTransferCategory_IsFiledUnderUncategorized_SoTheLinesStillAddUp()
    {
        Add(Day(2027, 1, 5), 50m, category: "TRANSFER_OUT");
        Add(Day(2027, 1, 6), 30m, category: null);
        Add(Day(2027, 1, 7), 5m, category: "INCOME");

        var result = await Reader().GetAsync(UserId, From, To);

        var line = Assert.Single(result.Months);
        Assert.Equal("UNCATEGORIZED", line.Line);
        Assert.Equal(85m, line.SpendUsd);
    }

    [Fact]
    public async Task MonthsBeforeTheWindowAreNotReturned_ButStillSetTheFirstDataDay()
    {
        Add(Day(2026, 11, 5), 99m);
        Add(Day(2027, 1, 5), 10m);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal(["2027-01"], result.Months.Select(m => m.Month));
        Assert.Equal(new DateOnly(2026, 11, 5), result.FirstDataDay);
    }

    [Fact]
    public async Task FirstDataDay_IsTheEarliestTransactionOfAnyKind()
    {
        Add(Day(2026, 11, 14), 2000m, FlowBuckets.Income, category: "INCOME");
        Add(Day(2027, 1, 5), 10m);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal(new DateOnly(2026, 11, 14), result.FirstDataDay);
    }

    [Fact]
    public async Task NoTransactions_HaveNoFirstDataDay_AndNothingToPlace()
    {
        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Null(result.FirstDataDay);
        Assert.Empty(result.Months);
        Assert.Empty(result.Commitments);
    }

    // -- commitments ----------------------------------------------------------------------------------------

    private void ActiveCommitment(
        string key, decimal amount, string currency = "USD", string cadence = "monthly", DateOnly? end = null,
        bool tracked = true, string name = "Gym")
        => _commitments
            .Setup(c => c.GetActiveCommitmentsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ActiveCommitment(key, name, tracked, SubscriptionKinds.Subscription, cadence, amount, currency, end)]);

    [Fact]
    public async Task ACommitment_IsPlacedOnTheLineItsLatestChargeLandedIn()
    {
        Add(Day(2027, 1, 5), 30m, category: "PERSONAL_CARE", merchant: "Gym");
        Add(Day(2027, 2, 5), 30m, category: "ENTERTAINMENT", merchant: "Gym");
        ActiveCommitment("gym", 32m, end: new DateOnly(2028, 1, 1));

        var result = await Reader().GetAsync(UserId, From, To);

        var placed = Assert.Single(result.Commitments);
        Assert.Equal("ENTERTAINMENT", placed.Line);
        Assert.Equal(32m, placed.MonthlyUsd);
        Assert.Equal(new DateOnly(2028, 1, 1), placed.EndDate);
    }

    [Fact]
    public async Task AnEndedCommitment_IsPlacedOnItsLineSeparately_FromTheActiveOnes()
    {
        Add(Day(2027, 1, 5), 200m, category: "LOAN_PAYMENTS", merchant: "Old Loan");
        Add(Day(2027, 2, 5), 30m, category: "ENTERTAINMENT", merchant: "Gym");
        ActiveCommitment("gym", 30m);
        _commitments
            .Setup(c => c.GetEndedCommitmentsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ActiveCommitment(
                "old loan", "Old Loan", true, SubscriptionKinds.Installment, "monthly", 200m, "USD", new DateOnly(2027, 1, 1))]);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal("ENTERTAINMENT", Assert.Single(result.Commitments).Line);
        var ended = Assert.Single(result.EndedCommitments);
        Assert.Equal("LOAN_PAYMENTS", ended.Line);
        Assert.Equal(200m, ended.MonthlyUsd);
        Assert.Equal(new DateOnly(2027, 1, 1), ended.EndDate);
    }

    [Fact]
    public async Task ACommitmentPaidAsAHouseholdTransfer_LandsInTheHouseholdLine_NotItsStoredCategory()
    {
        Add(Day(2027, 1, 5), 330m, category: "LOAN_PAYMENTS", flowRole: FlowRoles.Household, merchant: "Home Loan");
        ActiveCommitment("home loan", 330m, name: "Home Loan");

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal("HOUSEHOLD", Assert.Single(result.Commitments).Line);
    }

    [Fact]
    public async Task ACommitmentIsConvertedToUsd_FromItsOwnCurrency()
    {
        Add(Day(2027, 1, 5), 24m, merchant: "Gym");
        ActiveCommitment("gym", 1000m, currency: "UAH");

        var result = await Reader().GetAsync(UserId, From, To);

        // 1000 UAH at the fallback rate of 0.024.
        Assert.Equal(24m, Assert.Single(result.Commitments).MonthlyUsd);
    }

    [Fact]
    public async Task ACommitmentWithNoChargeInTheHistory_CannotBePlaced_AndIsLeftOut()
    {
        Add(Day(2027, 1, 5), 10m, merchant: "Corner Shop");
        ActiveCommitment("gym", 32m);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Empty(result.Commitments);
    }

    [Fact]
    public async Task OnlyMonthlyCommitmentsAreMonthlySpend()
    {
        Add(Day(2027, 1, 5), 120m, merchant: "Gym");
        ActiveCommitment("gym", 120m, cadence: "annual");

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Empty(result.Commitments);
    }

    [Fact]
    public async Task ALegacyHandTypedCommitment_IsKeyedByItsName_LikeTheCommittedOutflowPolicyDoes()
    {
        Add(Day(2027, 1, 5), 40m, category: "PERSONAL_CARE", merchant: "Sports Club");
        ActiveCommitment("manual:subscription:sports club", 41m, tracked: false, name: "Sports Club");

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal("PERSONAL_CARE", Assert.Single(result.Commitments).Line);
    }

    [Fact]
    public async Task ACommitmentIsPlacedFromChargesOlderThanTheWindow()
    {
        Add(Day(2026, 8, 5), 30m, category: "PERSONAL_CARE", merchant: "Gym");
        ActiveCommitment("gym", 32m);

        var result = await Reader().GetAsync(UserId, From, To);

        Assert.Equal("PERSONAL_CARE", Assert.Single(result.Commitments).Line);
    }
}

namespace FinanceSentry.Tests.Unit.BankSync.Application.Subscriptions;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FluentAssertions;
using Xunit;

/// <summary>
/// Pins both sides of the MCC-4829 split (#553): Monobank books loan and installment
/// repayments under the same wire-transfer MCC as a genuine card-to-card transfer, so the
/// classifier has to claim the repayments without claiming the transfers.
/// </summary>
public class LoanRepaymentClassifierTests
{
    private const int WireTransferMcc = 4829;

    /// <summary>The mortgage as Monobank renders it: a bare masked card number, no wording.</summary>
    private const string MortgageDescription = "516936******4992";
    private const decimal MortgageAmount = 14060.96m;

    private static readonly IReadOnlyList<ActiveInstallmentPlan> NoPlans = [];

    private static string? Classify(
        string? description, decimal amount, int? mcc = WireTransferMcc,
        string? transactionType = "debit", string? merchantName = null,
        IReadOnlyList<ActiveInstallmentPlan>? plans = null)
        => LoanRepaymentClassifier.Resolve(
            transactionType, merchantName, description, amount, mcc, plans ?? NoPlans);

    // ── Repayments recognised from the description alone ──────────────────────

    [Theory]
    [InlineData("Платіж ТОВ Алло - monomarket", 2999.95)]
    [InlineData("Платіж FOXTROT- monomarket", 144.95)]
    [InlineData("Щомісячний платіж telemart - monomarket", 6499.85)]
    [InlineData("Погашення наступного платежу RozetkaPay", 1500.00)]
    [InlineData("Повне погашення RozetkaPay", 8420.00)]
    [InlineData("Платіж Pandora", 1200.00)]
    public void MonomarketShapedDebits_AreLoanPayments_EvenWithNoDetectedPlan(
        string description, double amount)
    {
        Classify(description, (decimal)amount).Should().Be(CategoryKeys.LoanPayments);
    }

    // ── The mortgage: only reachable through the detected plan ────────────────

    [Fact]
    public void MortgageShapedDebit_IsLoanPayment_WhenItsPlanIsActive()
    {
        var plans = PlanFor(MortgageDescription, MortgageAmount);

        Classify(MortgageDescription, MortgageAmount, plans: plans)
            .Should().Be(CategoryKeys.LoanPayments);
    }

    [Fact]
    public void MortgageShapedDebit_IsNotClaimed_BeforeItsPlanIsDetected()
    {
        // A brand-new plan's first repayment predates detection: the classifier declines and
        // the MCC map calls it a transfer until the recategorization path re-resolves it.
        Classify(MortgageDescription, MortgageAmount).Should().BeNull();
    }

    // ── Genuine transfers on the same MCC stay transfers ──────────────────────

    [Theory]
    [InlineData("Переказ на картку", 5000.00)]
    [InlineData("Ліза ❤️", 3000.00)]
    [InlineData("мама", 2000.00)]
    [InlineData("Liudmyla Sychova", 1000.00)]
    [InlineData("Поповнення «Поточний»", 700.00)]
    public void GenuineTransferDebits_AreNotClaimed(string description, double amount)
    {
        Classify(description, (decimal)amount).Should().BeNull();
    }

    [Fact]
    public void GenuineTransferDebit_IsNotClaimed_WhileAnUnrelatedPlanIsActive()
    {
        // The mortgage plan being active must not spill onto every other MCC-4829 debit.
        var plans = PlanFor(MortgageDescription, MortgageAmount);

        Classify("Переказ на картку", 5000m, plans: plans).Should().BeNull();
    }

    // ── Direction ─────────────────────────────────────────────────────────────

    [Fact]
    public void CreditWithInstallmentWording_IsNotClaimed()
    {
        // An installment refund arrives on MCC 4829 too; it is money coming in, not a repayment.
        Classify("Повне погашення RozetkaPay", 8420m, transactionType: "credit").Should().BeNull();
    }

    [Fact]
    public void RowWithNoTransactionType_IsTreatedAsAnOutflow()
    {
        // Legacy rows carry a null type; SubscriptionDetectionJob reads them as debits too.
        Classify("Платіж ТОВ Алло - monomarket", 2999.95m, transactionType: null)
            .Should().Be(CategoryKeys.LoanPayments);
    }

    // ── The mortgage key the classifier matches is the key the detector stores ─

    [Fact]
    public void MatchesTheKeyTheDetectorActuallyStoresForAMaskedCardPlan()
    {
        // Drift guard: the classifier's masked-PAN branch is only correct relative to
        // SubscriptionDetectionJob. Run the real detector over a year of mortgage payments and
        // feed its stored key back in — asserting a literal key would let both sides drift.
        var rows = Enumerable.Range(0, 12)
            .Select(i => new SubscriptionDetectionJob.TxRow(
                Guid.NewGuid(), null, MortgageDescription, MortgageAmount,
                new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc).AddMonths(i),
                CategoryKeys.TransferOut, WireTransferMcc, "UAH"))
            .ToList();

        var detected = SubscriptionDetectionJob.DetectSubscriptions(rows).ToList();

        detected.Should().ContainSingle()
            .Which.Kind.Should().Be(SubscriptionKinds.Installment);

        var storedPlans = detected
            .Select(d => new ActiveInstallmentPlan(d.MerchantNameNormalized, d.AverageAmount))
            .ToList();

        Classify(MortgageDescription, MortgageAmount, plans: storedPlans)
            .Should().Be(CategoryKeys.LoanPayments);
    }

    // ── The stored key is only the card's BIN, so the amount has to discriminate ─

    [Fact]
    public void SmallTransferToADifferentCardOnTheSameBin_IsNotClaimed()
    {
        // MerchantNameNormalizer strips the trailing group, so 516936******4992 and
        // 516936******1111 both key as "516936" — every monobank card shares the mortgage's
        // key. Only the amount tells a ₴200 transfer to a friend apart from the mortgage.
        var plans = PlanFor(MortgageDescription, MortgageAmount);

        Classify("516936******1111", 200m, plans: plans).Should().BeNull();
    }

    [Fact]
    public void RepaymentAmountThatDriftedWithinThePlansBand_IsStillClaimed()
    {
        // Interest and FX move a mortgage instalment month to month; the detector accepted the
        // plan because the charges cluster, so a nearby amount is the same obligation.
        var plans = PlanFor(MortgageDescription, MortgageAmount);

        Classify(MortgageDescription, MortgageAmount * 1.05m, plans: plans)
            .Should().Be(CategoryKeys.LoanPayments);
    }

    private static IReadOnlyList<ActiveInstallmentPlan> PlanFor(string description, decimal amount)
        => [new ActiveInstallmentPlan(
            CommitmentKeyResolver.Resolve(null, description, amount, WireTransferMcc), amount)];
}

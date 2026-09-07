namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FluentAssertions;
using Xunit;

/// <summary>
/// Pins the ORDER of the one shared categorization ladder. Every rung is proved to beat the rung
/// below it with a fixture where the two disagree — an ordering that only holds by accident is
/// an ordering the next refactor breaks silently (#553).
/// </summary>
public class TransactionCategorizerTests
{
    private static readonly IReadOnlyList<ActiveInstallmentPlan> NoPlans = [];

    private static string? Categorize(
        string? description,
        int? mcc = null,
        string? providerCategory = null,
        string? transactionType = "debit",
        decimal amount = 100m,
        string? merchantName = null,
        IReadOnlyList<ActiveInstallmentPlan>? plans = null)
        => StubCategoryResolver.Categorizer.Categorize(
            new CategorizationSignals(description, merchantName, transactionType, amount, mcc, providerCategory),
            plans ?? NoPlans);

    // ── Rung 1: the runtime-editable keyword bridge outranks everything ──────────

    [Fact]
    public void KeywordBridge_BeatsTheMccMap()
    {
        // "amazon" is a curated keyword; MCC 4829 would otherwise say TRANSFER_OUT.
        Categorize("Amazon Marketplace", mcc: 4829).Should().Be(CategoryKeys.GeneralMerchandise);
    }

    [Fact]
    public void KeywordBridge_BeatsTheProviderCategory()
    {
        // An admin editing merchant_keywords must be able to override what the bank says.
        Categorize("Lidl Ireland Ltd", providerCategory: CategoryKeys.GeneralMerchandise)
            .Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public void KeywordBridge_BeatsTheLoanRule()
    {
        // "installment" is a recognizer marker and "amazon" a curated keyword, so both rungs
        // claim this row and disagree. The admin's override channel wins — a merchant selling
        // "in 3 installments" is retail, and the bridge is how that gets corrected without a
        // code change.
        Categorize("Amazon installment 3 of 12").Should().Be(CategoryKeys.GeneralMerchandise);
    }

    // ── Rung 2: loan / installment repayments ────────────────────────────────────

    [Fact]
    public void LoanRule_BeatsTheMcc4829TransferMapping()
    {
        Categorize("Платіж ТОВ Алло - monomarket", mcc: 4829, amount: 2999.95m)
            .Should().Be(CategoryKeys.LoanPayments);
    }

    [Fact]
    public void LoanRule_BeatsAProviderCategoryThatCallsItATransfer()
    {
        // A TrueLayer-shaped row: the bank classified it as a transfer, the wording says loan.
        Categorize("Car loan installment 3 of 24", providerCategory: CategoryKeys.TransferOut)
            .Should().Be(CategoryKeys.LoanPayments);
    }

    [Fact]
    public void LoanRule_ReachesTheMortgageThroughAnActivePlan()
    {
        const string maskedPan = "516936******4992";
        const decimal monthly = 14060.96m;
        var plan = new ActiveInstallmentPlan(
            CommitmentKeyResolver.Resolve(null, maskedPan, monthly, 4829), monthly);

        Categorize(maskedPan, mcc: 4829, amount: monthly, plans: [plan])
            .Should().Be(CategoryKeys.LoanPayments);
    }

    [Fact]
    public void LoanRule_DoesNotClaimACredit()
    {
        // An installment refund is money coming in; the rule is about outflow.
        Categorize("Платіж ТОВ Алло - monomarket", mcc: 4829, transactionType: "credit")
            .Should().Be(CategoryKeys.TransferOut);
    }

    // ── Rung 3: the provider's own classification ────────────────────────────────

    [Fact]
    public void ProviderCategory_BeatsTheDirectionalTransferPrefix()
    {
        // "To Go Sushi" starts with the outgoing-transfer prefix, but the bank called it food.
        Categorize("To Go Sushi", providerCategory: CategoryKeys.FoodAndDrink)
            .Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public void ProviderCategory_TheMapperCouldNotPlace_FallsThrough()
    {
        // Callers hand this rung an already-mapped canonical key; a wording the mapper could not
        // place arrives as UNCATEGORIZED, which is a miss, not a claim.
        Categorize("Some Shop", mcc: 5411, providerCategory: CategoryKeys.Uncategorized)
            .Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public void ProviderCategory_ThatIsNotACanonicalKey_FallsThrough()
    {
        // Defence in depth: should a caller ever pass a raw provider wording, canonical-key
        // validation rejects it rather than storing it verbatim.
        Categorize("Some Shop", mcc: 5411, providerCategory: "Groceries > Supermarket")
            .Should().Be(CategoryKeys.FoodAndDrink);
    }

    // ── Rung 4: the directional-transfer / savings-jar description ───────────────

    [Fact]
    public void TransferDescription_BeatsTheMccMap()
    {
        // Monobank tags savings-jar top-ups with the charity MCC 8398; the description is the
        // only trustworthy signal that the money never left the user's own book.
        Categorize("Поповнення «Поточний»", mcc: 8398).Should().Be(CategoryKeys.TransferOut);
    }

    [Fact]
    public void GenuineCardToCardTransfer_StaysTransferOut()
    {
        Categorize("Переказ на картку", mcc: 4829, amount: 5000m).Should().Be(CategoryKeys.TransferOut);
    }

    // ── Rung 5 and the floor ─────────────────────────────────────────────────────

    [Fact]
    public void MccMap_ClaimsWhatNothingAboveItDid()
    {
        Categorize("ATB Market", mcc: 5411).Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public void NoSignalAtAll_ClaimsNothing()
    {
        // Null, not UNCATEGORIZED: the backfill needs to tell "no rule matched" from a stored
        // miss, so the row stays eligible for a provider re-fetch.
        Categorize("Ліза ❤️").Should().BeNull();
    }

    [Fact]
    public void AnMccThatMapsToNothing_ClaimsNothing()
    {
        Categorize("Unknown merchant", mcc: 1).Should().BeNull();
    }
}

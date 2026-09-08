namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The committed/discretionary definition in isolation (spec 554). Fixtures are shaped like
/// the rows the definition was widened for: the rent transfer that no detector ever promotes
/// to a subscription, the mortgage behind a masked card, a monomarket installment, a
/// Netflix-style subscription and an ordinary shop.
/// </summary>
public class CommittedOutflowPolicyTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Transaction Debit(
        decimal amount, string description, string? merchantName = null,
        string? category = null, int? mcc = null) =>
        new(Guid.NewGuid(), UserId, amount, new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            description, Guid.NewGuid().ToString("N"), false)
        {
            TransactionType = "debit",
            MerchantName = merchantName,
            MerchantCategory = category,
            Mcc = mcc,
        };

    private static CommittedOutflowRules Rules(params string[] activeCommitmentKeys) =>
        new(activeCommitmentKeys.ToHashSet(StringComparer.Ordinal), NoPins);

    private static CommittedOutflowRules RulesWithPins(params string[] pinnedMerchantKeys) =>
        new(NoPins, pinnedMerchantKeys.ToHashSet(StringComparer.Ordinal));

    private static IReadOnlySet<string> NoPins => new HashSet<string>(StringComparer.Ordinal);

    // ── Rule (a): an active detected commitment ──────────────────────────────

    [Fact]
    public void IsCommitted_ActiveSubscriptionKey_IsCommitted()
    {
        var netflix = Debit(15.99m, "CARD PAYMENT 4471", merchantName: "Netflix.com");

        Rules("netflix").IsCommitted(netflix).Should().BeTrue();
    }

    [Fact]
    public void IsCommitted_ActiveInstallmentPlanKey_IsCommitted()
    {
        // Keyed installment:{merchant}:{roundedAmount} by the detector, not by merchant name —
        // a rule set holding only the merchant key must NOT claim it.
        var repayment = Debit(6499.84m, "Щомісячний платіж telemart - monomarket");

        Rules("installment:telemart:6500").IsCommitted(repayment).Should().BeTrue();
        Rules("telemart").IsCommitted(repayment).Should().BeFalse();
    }

    [Fact]
    public void IsCommitted_SubscriptionThatIsNoLongerActive_IsDiscretionary()
    {
        var netflix = Debit(15.99m, "CARD PAYMENT 4471", merchantName: "Netflix.com");

        Rules("spotify").IsCommitted(netflix).Should().BeFalse();
    }

    // ── Rule (b): a committed category ───────────────────────────────────────

    [Fact]
    public void IsCommitted_Rent_IsCommittedWithoutAnyDetectedSubscription()
    {
        // The 1,275 EUR/mo rent transfer: same payee, same amount, never a DetectedSubscription.
        // This is the row the whole widening exists for, so it must hold on an EMPTY rule set.
        var rent = Debit(1275m, "To Mario Scalas", category: CategoryKeys.RentAndUtilities);

        Rules().IsCommitted(rent).Should().BeTrue();
    }

    [Fact]
    public void IsCommitted_LoanPayment_IsCommittedWithoutAnyDetectedSubscription()
    {
        // A repayment #553 categorized at the source but the detector never grouped into a plan
        // (a first instalment, or one whose amount drifted out of its plan's band).
        var repayment = Debit(14060.96m, "516936******4992",
            category: CategoryKeys.LoanPayments, mcc: 4829);

        Rules().IsCommitted(repayment).Should().BeTrue();
    }

    [Theory]
    [InlineData(CategoryKeys.FoodAndDrink)]
    [InlineData(CategoryKeys.GeneralMerchandise)]
    [InlineData(CategoryKeys.Entertainment)]
    [InlineData(CategoryKeys.Travel)]
    [InlineData(CategoryKeys.Uncategorized)]
    public void IsCommitted_OrdinarySpendCategories_AreDiscretionary(string category)
    {
        var shop = Debit(62.40m, "ZARA MILANO", merchantName: "Zara", category: category);

        Rules().IsCommitted(shop).Should().BeFalse();
    }

    [Fact]
    public void IsCommitted_UncategorizedOneOffShop_IsDiscretionary()
    {
        // No category at all, no commitment key — the default side of the partition.
        Rules("netflix").IsCommitted(Debit(23.10m, "СІЛЬПО")).Should().BeFalse();
    }

    [Fact]
    public void IsCommitted_MobileTopUp_IsCommitted_TheAcceptedOverClaim()
    {
        // Pins the boundary the category rule cannot see: the ingest ladder files a mobile
        // top-up under RENT_AND_UTILITIES (the `top-up` keyword and the 4812–4900 telecom MCC
        // range), so an ad-hoc top-up reads as committed exactly like the monthly phone plan.
        // Recorded as an accepted over-claim, not an accident — see CommittedCategories.
        var topUp = Debit(10m, "*MOBI TOP-UP 0857860057",
            category: CategoryKeys.RentAndUtilities, mcc: 4814);

        Rules().IsCommitted(topUp).Should().BeTrue();
    }

    // ── Rule (c): a counterparty obligation ──────────────────────────────────

    [Theory]
    [InlineData(FlowRoles.FamilySupport)]
    [InlineData(FlowRoles.Household)]
    public void IsCommittedFlowRole_StandingObligations_AreCommitted(string flowRole)
    {
        Rules().IsCommittedFlowRole(flowRole).Should().BeTrue();
    }

    [Theory]
    [InlineData(FlowRoles.Investment)]
    [InlineData(FlowRoles.SelfRouting)]
    [InlineData("")]
    [InlineData(null)]
    public void IsCommittedFlowRole_EverythingElse_IsNotCommitted(string? flowRole)
    {
        // The empty role is the live case, not a defensive one: Counterparty.FlowRole defaults
        // to string.Empty, so a counterparty saved without a role is spending that names no
        // obligation and must land discretionary.
        Rules().IsCommittedFlowRole(flowRole).Should().BeFalse();
    }

    // ── Rule (d): a user pin ─────────────────────────────────────────────────

    [Fact]
    public void IsCommitted_PinnedMerchant_IsCommittedWithoutCategoryOrDetection()
    {
        // The obligation only the user can see: a standing payment to a person, uncategorized,
        // never promoted to a subscription. It is committed because — and only because — the
        // user said so.
        var standingPayment = Debit(400m, "To Mario Scalas", merchantName: "Mario Scalas");

        RulesWithPins("mario scalas").IsCommitted(standingPayment).Should().BeTrue();
        Rules().IsCommitted(standingPayment).Should().BeFalse();
    }

    [Fact]
    public void IsCommitted_PinIsMatchedOnTheDetectionKey_SoEverySpellingCounts()
    {
        // One pin has to claim every spelling the statement uses. "NETFLIX.COM" and "Netflix"
        // normalize to the same detection key, which is why the pin stores the key rather than
        // the typed name.
        var pinned = RulesWithPins("netflix");

        pinned.IsCommitted(Debit(15.99m, "CARD PAYMENT", merchantName: "NETFLIX.COM")).Should().BeTrue();
        pinned.IsCommitted(Debit(15.99m, "CARD PAYMENT", merchantName: "Netflix")).Should().BeTrue();
    }

    [Fact]
    public void IsCommitted_PinnedMerchantsInstallmentShapedRow_IsStillCommitted()
    {
        // The reason rule (d) re-derives instead of reusing the rule (a) key: the resolver keys
        // this row installment:telemart:6500, which no merchant pin can ever equal — so a pinned
        // merchant's repayments would slip through to discretionary. Rule (d) keys on the
        // merchant, so it claims them.
        var repayment = Debit(6499.84m, "Щомісячний платіж telemart - monomarket",
            merchantName: "Telemart");

        RulesWithPins("telemart").IsCommitted(repayment).Should().BeTrue();
        Rules("telemart").IsCommitted(repayment).Should().BeFalse(
            because: "rule (a) sees only the installment plan key for this row");
    }

    [Fact]
    public void IsCommitted_PinDoesNotMatchARowThatNamesTheMerchantOnlyInsideItsDescription()
    {
        // A known limit of rule (d), pinned so it cannot regress silently into a fuzzy match.
        // Rows with no MerchantName key off the WHOLE description, so a pin on "Telemart" does
        // not claim "Щомісячний платіж telemart - monomarket" — the pin is an exact key match,
        // not a substring search. Anything looser would let a pin on a common word ("bank",
        // "card") swallow unrelated spend.
        var descriptionOnly = Debit(6499.84m, "Щомісячний платіж telemart - monomarket");

        RulesWithPins("telemart").IsCommitted(descriptionOnly).Should().BeFalse();
    }

    [Fact]
    public void IsCommitted_MerchantTheUserDidNotPin_IsDiscretionary()
    {
        // A pin is a claim about ONE merchant, never a mood about the month.
        RulesWithPins("mario scalas")
            .IsCommitted(Debit(62.40m, "ZARA MILANO", merchantName: "Zara"))
            .Should().BeFalse();
    }

    [Fact]
    public void IsCommitted_UnnamedDebit_IsNotClaimedByAPinOnTheUnknownKey()
    {
        // Defence for the hole CommittedMerchantKey.Derive closes at the write side: every
        // unnameable debit normalizes to "unknown", so a rule set that somehow held that key
        // would silently claim the whole unnamed tail of the book. Even then, it must not.
        var unnamed = Debit(31.20m, "   ");

        RulesWithPins(MerchantNameNormalizer.UnknownKey).IsCommitted(unnamed).Should().BeFalse();
    }

    // ── Loading the per-user rule set ────────────────────────────────────────

    [Fact]
    public async Task LoadForUserAsync_CarriesTheUsersActiveCommitmentKeysIntoTheRules()
    {
        var rules = await Policy(
            commitmentKeys: ["installment:telemart:6500"], pinnedKeys: []).LoadForUserAsync(UserId);

        rules.IsCommitted(Debit(6499.84m, "Щомісячний платіж telemart - monomarket"))
             .Should().BeTrue();
    }

    [Fact]
    public async Task LoadForUserAsync_CarriesTheUsersPinsIntoTheRules()
    {
        var rules = await Policy(
            commitmentKeys: [], pinnedKeys: ["mario scalas"]).LoadForUserAsync(UserId);

        rules.IsCommitted(Debit(400m, "To Mario Scalas", merchantName: "Mario Scalas"))
             .Should().BeTrue();
    }

    [Fact]
    public async Task LoadForUserAsync_ReadsEachSourceOncePerUser_NotOncePerTransaction()
    {
        var subscriptions = new Mock<IActiveSubscriptionsReader>();
        subscriptions.Setup(r => r.GetActiveCommitmentMerchantKeysAsync(UserId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));
        var pins = new Mock<ICommittedMerchantPinRepository>();
        pins.Setup(p => p.GetPinnedKeysAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));

        var rules = await new CommittedOutflowPolicy(subscriptions.Object, pins.Object)
            .LoadForUserAsync(UserId);
        for (var i = 0; i < 3; i++)
            rules.IsCommitted(Debit(10m + i, "СІЛЬПО"));

        subscriptions.Verify(
            r => r.GetActiveCommitmentMerchantKeysAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        pins.Verify(p => p.GetPinnedKeysAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CommittedOutflowPolicy Policy(string[] commitmentKeys, string[] pinnedKeys)
    {
        var subscriptions = new Mock<IActiveSubscriptionsReader>();
        subscriptions.Setup(r => r.GetActiveCommitmentMerchantKeysAsync(UserId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(commitmentKeys.ToHashSet(StringComparer.Ordinal));

        var pins = new Mock<ICommittedMerchantPinRepository>();
        pins.Setup(p => p.GetPinnedKeysAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pinnedKeys.ToHashSet(StringComparer.Ordinal));

        return new CommittedOutflowPolicy(subscriptions.Object, pins.Object);
    }
}

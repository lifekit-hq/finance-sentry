namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using System.Reflection;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;

public class DetectionPhraseSetTests
{
    private static string[] Kinds() =>
        typeof(DetectionPhraseKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

    [Fact]
    public void Seed_UsesOnlyKnownKinds_AndEveryKindHasRows()
    {
        var seeded = DetectionPhraseSeedData.Entries.Select(e => e.Kind).Distinct().ToList();

        seeded.Should().BeEquivalentTo(Kinds());
    }

    [Fact]
    public void Seed_HasNoDuplicateKindPhrase_AndFitsTheColumns()
    {
        DetectionPhraseSeedData.Entries.GroupBy(e => (e.Kind, e.Phrase)).Where(g => g.Count() > 1)
            .Select(g => g.Key).Should().BeEmpty("the table is unique on (kind, phrase)");

        foreach (var e in DetectionPhraseSeedData.Entries)
        {
            e.Kind.Length.Should().BeLessThanOrEqualTo(50);
            e.Phrase.Length.Should().BeLessThanOrEqualTo(100);
            (e.Provider?.Length ?? 0).Should().BeLessThanOrEqualTo(50);
            (e.Locale?.Length ?? 0).Should().BeLessThanOrEqualTo(10);
        }
    }

    [Fact]
    public void Seed_ShipsTheFoldedInWording()
    {
        var seed = DetectionPhraseSet.Seed;

        seed.Phrases(DetectionPhraseKinds.NotAMerchant).Should().Contain("переказ");
        seed.Phrases(DetectionPhraseKinds.CashLodgementMarker).Should().Equal("*ATMLDG");
        seed.Phrases(DetectionPhraseKinds.TransferOutPrefix).Should().Equal("To ", "Поповнення ");
        seed.Phrases(DetectionPhraseKinds.TransferInPrefix).Should().Equal("From ", "З банки ");
        seed.Phrases(DetectionPhraseKinds.IncomePrefix).Should().Equal("Payment from ");
    }

    [Fact]
    public void Phrases_OfAnUnknownKind_AreEmpty()
    {
        DetectionPhraseSet.Seed.Phrases("no_such_kind").Should().BeEmpty();
    }

    [Fact]
    public void Phrases_KeepRowOrder()
    {
        var set = new DetectionPhraseSet([
            new("transfer_out_prefix", "Zeta "), new("transfer_out_prefix", "Alpha "), new("transfer_out_prefix", "Mid ")]);

        set.Phrases("transfer_out_prefix").Should().Equal("Zeta ", "Alpha ", "Mid ");
    }

    [Fact]
    public void Phrases_OfLowercaseKinds_AreLowercased_OthersKeepTheirCase()
    {
        var set = new DetectionPhraseSet([
            new(DetectionPhraseKinds.InstallmentMarker, "РОЗСТРОЧ"),
            new(DetectionPhraseKinds.NotAMerchant, "ATM"),
            new(DetectionPhraseKinds.TransferOutPrefix, "To ")]);

        set.Phrases(DetectionPhraseKinds.InstallmentMarker).Should().Equal("розстроч");
        set.Phrases(DetectionPhraseKinds.NotAMerchant).Should().Equal("atm");
        set.Phrases(DetectionPhraseKinds.TransferOutPrefix).Should().Equal("To ");
    }

    [Fact]
    public void Phrases_WithAMccCondition_ApplyOnlyToThatMcc()
    {
        var set = new DetectionPhraseSet([
            new("k", "always"), new("k", "wire only", MccCondition: 4829)]);

        set.Phrases("k", mcc: 4829).Should().Equal("always", "wire only");
        set.Phrases("k", mcc: 5411).Should().Equal("always");
        set.Phrases("k", mcc: null).Should().Equal("always");
    }

    [Fact]
    public void Phrases_WithACategoryCondition_ApplyOnlyToThatCategory()
    {
        var set = new DetectionPhraseSet([
            new("k", "always"), new("k", "dining only", CategoryCondition: CategoryKeys.FoodAndDrink)]);

        set.Phrases("k", category: CategoryKeys.FoodAndDrink).Should().Equal("always", "dining only");
        set.Phrases("k", category: CategoryKeys.Income).Should().Equal("always");
        set.Phrases("k", category: null).Should().Equal("always");
    }

    [Fact]
    public void Phrases_WithBothConditions_NeedBoth()
    {
        var set = new DetectionPhraseSet([new("k", "both", MccCondition: 5411, CategoryCondition: CategoryKeys.FoodAndDrink)]);

        set.Phrases("k", 5411, CategoryKeys.FoodAndDrink).Should().Equal("both");
        set.Phrases("k", 5411, CategoryKeys.Income).Should().BeEmpty();
        set.Phrases("k", 4829, CategoryKeys.FoodAndDrink).Should().BeEmpty();
    }

    [Fact]
    public void EmptyPhrases_AreDropped()
    {
        var set = new DetectionPhraseSet([new("k", ""), new("k", "x")]);

        set.Phrases("k").Should().Equal("x");
    }

    // --- the table, not the code, decides ---

    [Fact]
    public void AnAddedInstallmentMarker_ChangesWhatIsRecognised()
    {
        var polish = new DetectionPhraseSet(DetectionPhraseSeedData.Entries
            .Append(new(DetectionPhraseKinds.InstallmentMarker, "rata ", Locale: "pl")));

        InstallmentPlanRecognizer.IsInstallmentDescription(DetectionPhraseSet.Seed, "Rata 3/12 Media Markt").Should().BeFalse();
        InstallmentPlanRecognizer.IsInstallmentDescription(polish, "Rata 3/12 Media Markt").Should().BeTrue();
    }

    [Fact]
    public void AnAddedTransferPrefix_ChangesTheCategory()
    {
        var german = new DetectionPhraseSet(DetectionPhraseSeedData.Entries
            .Append(new(DetectionPhraseKinds.TransferOutPrefix, "Überweisung an ", Locale: "de")));

        TransferDescriptionClassifier.Resolve(DetectionPhraseSet.Seed, "Überweisung an Max").Should().BeNull();
        TransferDescriptionClassifier.Resolve(german, "Überweisung an Max").Should().Be(CategoryKeys.TransferOut);
    }

    [Fact]
    public void ARemovedLodgementMarker_StopsClaimingTheCredit()
    {
        var withoutMarker = new DetectionPhraseSet(DetectionPhraseSeedData.Entries
            .Where(e => e.Kind != DetectionPhraseKinds.CashLodgementMarker));

        CashLodgementClassifier.Resolve(DetectionPhraseSet.Seed, "*ATMLDG CAPEL ST", "credit").Should().Be(CategoryKeys.Income);
        CashLodgementClassifier.Resolve(withoutMarker, "*ATMLDG CAPEL ST", "credit").Should().BeNull();
    }

    [Fact]
    public void ARemovedNotAMerchantWord_LetsThatKeyThrough()
    {
        var withoutTransfer = new DetectionPhraseSet(DetectionPhraseSeedData.Entries
            .Where(e => !(e.Kind == DetectionPhraseKinds.NotAMerchant && e.Phrase == "переказ")));

        SubscriptionDetectionAlgorithm.IsUnidentifiableMerchant(DetectionPhraseSet.Seed, "переказ").Should().BeTrue();
        SubscriptionDetectionAlgorithm.IsUnidentifiableMerchant(withoutTransfer, "переказ").Should().BeFalse();
    }

    [Fact]
    public void TheMccConditionOfThePrefixRow_IsWhatGatesIt()
    {
        var anyMcc = new DetectionPhraseSet(DetectionPhraseSeedData.Entries
            .Select(e => e.Kind == DetectionPhraseKinds.InstallmentTransferPrefix ? e with { MccCondition = null } : e));

        InstallmentPlanRecognizer.IsInstallmentTransaction(DetectionPhraseSet.Seed, "Платіж Pandora", 5411).Should().BeFalse();
        InstallmentPlanRecognizer.IsInstallmentTransaction(anyMcc, "Платіж Pandora", 5411).Should().BeTrue();
        InstallmentPlanRecognizer.IsInstallmentTransaction(DetectionPhraseSet.Seed, "Платіж Pandora", 4829).Should().BeTrue();
    }

    [Fact]
    public void Seed_ContainsNoPersonalData()
    {
        // Generic statement vocabulary only: wording, markers and prefixes. A seed row must never
        // carry a person, an account or card number, an address or a household's own words.
        foreach (var e in DetectionPhraseSeedData.Entries)
        {
            e.Phrase.Any(char.IsDigit).Should().BeFalse($"'{e.Phrase}' must not hold a number");
            e.Phrase.Should().NotContain("@");
        }
    }
}

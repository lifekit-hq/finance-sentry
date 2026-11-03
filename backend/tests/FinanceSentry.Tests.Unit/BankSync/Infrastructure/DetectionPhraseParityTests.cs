namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;

/// <summary>
/// Moving the detection vocabulary into <c>detection_phrases</c> must not change a single outcome.
/// Each test runs the old hard-coded path (<see cref="LegacyDetectionVocabulary"/>) and the
/// table-driven classes (over <see cref="DetectionPhraseSet.Seed"/>) over the same inputs and asserts
/// they agree on every one.
/// </summary>
public class DetectionPhraseParityTests
{
    private static readonly DetectionPhraseSet Phrases = DetectionPhraseSet.Seed;

    private static readonly string?[] BaseDescriptions =
    [
        // The vocabulary itself, in its shipped form.
        "Погашення наступного платежу RozetkaPay", "Повне погашення RozetkaPay",
        "Щомісячний платіж telemart - monomarket", "Погашення наступного платежу ТОВ Алло - monomarket",
        "Платіж Pandora", "Платіж", "Платіж  Pandora", "платіж pandora", "  Платіж Pandora ",
        "Оплата частинами Rozetka", "Покупка частинами Foxtrot", "Купівля у розстрочку Comfy",
        "Розстрочка Allo", "Installment plan Apple", "INSTALLMENT 3/12", "- monomarket", "MONOMARKET",
        "Переказ на картку", "переказ", "Переказ на картку 5375****1234", "400000******0002",
        "To Mario Scalas", "to someone", "  To Someone", "To", "Tobacco Shop", "Tommy Hilfiger",
        "From Instant Access Savings", "FROM savings", "Fromage Cheese Co", "From",
        "Payment from Andrea Di Florio", "payment from x", "Payment fromage",
        "Поповнення «Поточний RUSORIZ»", "поповнення «Jar»", "Поповнення", "З банки «Поточний RUSORIZ»",
        "з банки «Jar»", "З банки",
        "*ATMLDG CAPEL ST", "*ATMLDG EXCISE WAL", "  *atmldg Capel St", "*ATMWDL CAPEL ST", "ATMLDG", "x *ATMLDG",
        // Ordinary merchants and near misses.
        "Lidl Ireland Ltd", "Netflix.com", "CARD PAYMENT 4471", "To Go Sushi", "Apple Store", "Uber Eats",
        "Поповнення мобільного", "Платіжна система", "Частина покупки", "Повне погашення", "повне ПОГАШЕННЯ x",
        "Погашення", "Щомісячний", "", "   ", null,
    ];

    private static readonly string[] Decorations = ["{0}", "  {0}", "{0}  ", "\t{0}", "{0} - monomarket", "{0} 12/12", "Платіж {0}", "To {0}"];

    private static readonly int?[] Mccs = [null, 4829, 4814, 4900, 5411, 8398, 0];

    private static readonly string?[] TransactionTypes = [null, "credit", "debit", "CREDIT", "Credit", ""];

    private static IEnumerable<string?> Corpus()
    {
        foreach (var d in BaseDescriptions)
        {
            if (d is null)
            {
                yield return null;
                continue;
            }

            foreach (var decoration in Decorations)
                yield return string.Format(decoration, d);
        }

        // Every shipped phrase, bare and embedded, whatever its kind: a new row cannot dodge the corpus.
        foreach (var entry in DetectionPhraseSeedData.Entries)
        {
            yield return entry.Phrase;
            yield return entry.Phrase.ToUpperInvariant();
            yield return entry.Phrase + "Acme";
            yield return "Acme " + entry.Phrase;
            yield return "  " + entry.Phrase + " Acme";
        }
    }

    [Fact]
    public void Corpus_IsLargeEnoughToMeanSomething()
    {
        Corpus().Count().Should().BeGreaterThan(500);
    }

    [Fact]
    public void IsInstallmentDescription_AgreesWithTheLegacyMarkers()
    {
        foreach (var description in Corpus())
        {
            InstallmentPlanRecognizer.IsInstallmentDescription(Phrases, description)
                .Should().Be(LegacyDetectionVocabulary.IsInstallmentDescription(description), $"description {description ?? "<null>"}");
        }
    }

    [Fact]
    public void IsInstallmentTransaction_AgreesWithTheLegacyMarkersAndMccRule()
    {
        foreach (var description in Corpus())
        {
            foreach (var mcc in Mccs)
            {
                InstallmentPlanRecognizer.IsInstallmentTransaction(Phrases, description, mcc)
                    .Should().Be(LegacyDetectionVocabulary.IsInstallmentTransaction(description, mcc), $"description {description ?? "<null>"}, mcc {mcc}");
            }
        }
    }

    [Fact]
    public void IsInstallmentPayoff_AgreesWithTheLegacyMarker()
    {
        foreach (var description in Corpus())
        {
            InstallmentPlanRecognizer.IsInstallmentPayoff(Phrases, description)
                .Should().Be(LegacyDetectionVocabulary.IsInstallmentPayoff(description), $"description {description ?? "<null>"}");
        }
    }

    [Fact]
    public void ExtractMerchant_AgreesWithTheLegacyPrefixesAndTag()
    {
        foreach (var description in Corpus().Where(d => d is not null))
        {
            InstallmentPlanRecognizer.ExtractMerchant(Phrases, description!)
                .Should().Be(LegacyDetectionVocabulary.ExtractMerchant(description!), $"description {description}");
        }
    }

    [Fact]
    public void PlanKeyForTransaction_AgreesWithTheLegacyChain()
    {
        foreach (var description in Corpus())
        {
            foreach (var mcc in Mccs)
            {
                var legacy = LegacyPlanKey(description, 100m, mcc);

                InstallmentPlanRecognizer.PlanKeyForTransaction(Phrases, description, 100m, mcc)
                    .Should().Be(legacy, $"description {description ?? "<null>"}, mcc {mcc}");
            }
        }
    }

    private static string? LegacyPlanKey(string? description, decimal amount, int? mcc)
    {
        if (!LegacyDetectionVocabulary.IsInstallmentTransaction(description, mcc)) return null;
        if (LegacyDetectionVocabulary.IsInstallmentPayoff(description)) return null;

        var merchant = LegacyDetectionVocabulary.ExtractMerchant(description ?? string.Empty);
        return string.IsNullOrWhiteSpace(merchant) ? null : InstallmentPlanRecognizer.PlanKey(merchant, amount);
    }

    [Fact]
    public void IsUnidentifiableMerchant_AgreesWithTheLegacyWords()
    {
        var keys = Corpus().Where(d => d is not null).Select(d => d!.ToLowerInvariant())
            .Concat(["mobile top-up 0057", "mobile top-up", "unknown", "netflix", "atm", "catmint", "cashew", "transferwise", "переказ", "без переказу", "recharge.io", ""])
            .Distinct().ToList();

        foreach (var key in keys)
        {
            SubscriptionDetectionAlgorithm.IsUnidentifiableMerchant(Phrases, key)
                .Should().Be(LegacyDetectionVocabulary.IsUnidentifiableMerchant(key), $"key '{key}'");
        }
    }

    [Fact]
    public void CashLodgement_AgreesWithTheLegacyMarker()
    {
        foreach (var description in Corpus())
        {
            foreach (var type in TransactionTypes)
            {
                CashLodgementClassifier.Resolve(Phrases, description, type)
                    .Should().Be(LegacyDetectionVocabulary.ResolveCashLodgement(description, type), $"description {description ?? "<null>"}, type {type ?? "<null>"}");
            }
        }
    }

    [Fact]
    public void TransferDescription_AgreesWithTheLegacyPrefixes()
    {
        foreach (var description in Corpus())
        {
            TransferDescriptionClassifier.Resolve(Phrases, description)
                .Should().Be(LegacyDetectionVocabulary.ResolveTransferDescription(description), $"description {description ?? "<null>"}");
        }
    }

    [Fact]
    public void ParityIsNotVacuous_EveryClassifierFiresOnSomethingInTheCorpus()
    {
        var corpus = Corpus().ToList();

        corpus.Any(d => InstallmentPlanRecognizer.IsInstallmentDescription(Phrases, d)).Should().BeTrue();
        corpus.Any(d => InstallmentPlanRecognizer.IsInstallmentTransaction(Phrases, d, 4829) && !InstallmentPlanRecognizer.IsInstallmentDescription(Phrases, d)).Should().BeTrue();
        corpus.Any(d => InstallmentPlanRecognizer.IsInstallmentPayoff(Phrases, d)).Should().BeTrue();
        corpus.Any(d => CashLodgementClassifier.Resolve(Phrases, d, "credit") is not null).Should().BeTrue();
        corpus.Select(d => TransferDescriptionClassifier.Resolve(Phrases, d)).Where(r => r is not null).Distinct().Should().HaveCount(3);
    }
}

namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;

/// <summary>
/// Exercises the range classifier against the full embedded MCC dataset to guard the
/// seeded <c>mcc_categories</c> output: every code resolves to a valid category and the
/// bulk of real merchant codes are classified rather than dropped to UNCATEGORIZED.
/// </summary>
public class MccDatasetCoverageTests
{
    private static readonly IReadOnlySet<string> ValidKeys =
        CategorySeedData.Categories.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

    private static List<int> LoadMccs()
    {
        var assembly = typeof(MccRangeClassifier).Assembly;
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("mcc_codes.csv", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        reader.ReadLine(); // header

        var mccs = new List<int>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var first = line.Split(',', 2)[0].Trim('"', ' ');
            if (int.TryParse(first, out var mcc))
                mccs.Add(mcc);
        }
        return mccs;
    }

    [Fact]
    public void EveryDatasetCode_ResolvesToAValidCategory()
    {
        var mccs = LoadMccs();
        mccs.Should().HaveCountGreaterThan(900, "the greggles dataset ships ~980 codes");

        foreach (var mcc in mccs)
            ValidKeys.Should().Contain(MccRangeClassifier.Classify(mcc));
    }

    [Fact]
    public void MostCodes_AreClassified_NotDroppedToUncategorized()
    {
        var mccs = LoadMccs();
        var uncategorized = mccs.Count(m => MccRangeClassifier.Classify(m) == CategoryKeys.Uncategorized);

        var fraction = (double)uncategorized / mccs.Count;
        fraction.Should().BeLessThan(0.20, "range rules should cover the large majority of MCCs");
    }

    [Fact]
    public void NoDatasetCode_IsLeftUncategorized()
    {
        // A code the rules miss seeds an UNCATEGORIZED row on a fresh database; the live table
        // then drifts from code as soon as someone fixes the row by hand.
        LoadMccs().Where(m => MccRangeClassifier.Classify(m) == CategoryKeys.Uncategorized)
            .Should().BeEmpty();
    }
}

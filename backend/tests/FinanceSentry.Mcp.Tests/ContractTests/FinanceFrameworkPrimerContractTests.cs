using System.Text.RegularExpressions;
using FinanceSentry.Mcp.Resources;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Mcp.Tests.ContractTests;

/// <summary>
/// Pins the hand-written framework primer (<see cref="FinanceFrameworkPrimerResource"/>) to the docs it
/// distils, so a money-math doc change that moves an anchor fails here until the primer follows: the ten
/// measure names and the headline contract's methods and qualities match <c>docs/finance-framework.md</c>,
/// its decided and open call ids match §1, and every section it cites still exists.
/// </summary>
public sealed partial class FinanceFrameworkPrimerContractTests
{
    private static readonly string Primer = FinanceFrameworkPrimerResource.Read();
    private static readonly string PrimerOneLine = Regex.Replace(Primer, @"\s+", " ");
    private static readonly string Framework = ReadRepoFile("docs", "finance-framework.md");
    private static readonly string MoneySemantics = ReadRepoFile("docs", "money-semantics.md");

    [Fact]
    public void Primer_StaysShort()
    {
        Primer.Length.Should().BeInRange(4_000, 8_000, "the primer is the short map; the long docs carry the detail");
    }

    [Fact]
    public void Primer_NamesTheTenMeasures_AsTheFrameworkDoes()
    {
        var docMeasures = MeasureRow().Matches(Section(Framework, "### 3.4 The ten measures"))
            .Select(m => $"{m.Groups["id"].Value} {m.Groups["name"].Value.Trim()}")
            .ToList();
        var primerMeasures = PrimerMeasure().Matches(Primer).Select(m => m.Groups["measure"].Value).ToList();

        docMeasures.Should().HaveCount(10);
        primerMeasures.Should().Equal(docMeasures);
    }

    [Fact]
    public void Primer_CarriesTheHeadlineContract_MethodsAndQualities()
    {
        var contract = Section(Framework, "### 3.6 The headline contract");
        Primer.Should().Contain("## The headline contract (finance-framework §3.6)");

        foreach (var field in new[] { "method", "quality" })
        {
            var values = Regex.Match(contract, $@"^  {field}\s+(?<values>[A-Z_ |]+)$", RegexOptions.Multiline)
                .Groups["values"].Value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            values.Should().NotBeEmpty($"the headline record in §3.6 lists its {field} values");
            PrimerOneLine.Should().Contain($"`{field}` ({string.Join(", ", values)})");
        }
    }

    [Fact]
    public void Primer_ListsTheDecidedAndOpenCalls_AsSectionOneDoes()
    {
        var decided = CallIds(Section(Framework, "### Decided"));
        var open = CallIds(Section(Framework, "### Open"));

        decided.Should().NotBeEmpty();
        open.Should().NotBeEmpty();
        PrimerOneLine.Should().Contain($"Decided: {string.Join(", ", decided)}.");
        PrimerOneLine.Should().Contain($"Open: {string.Join(", ", open)}.");
    }

    [Fact]
    public void Primer_CitesOnlySectionsThatExist()
    {
        var frameworkSections = SectionNumbers(Framework);
        var moneySections = SectionNumbers(MoneySemantics);

        var citations = Citation().Matches(Primer).ToList();
        citations.Should().NotBeEmpty();
        foreach (Match citation in citations)
        {
            var known = citation.Groups["doc"].Value == "finance-framework" ? frameworkSections : moneySections;
            foreach (var number in SectionList().Matches(citation.Groups["sections"].Value).Select(m => m.Value))
                known.Should().Contain(number, $"the primer cites {citation.Value}");
        }

        ReadRepoFile(".specify", "memory", "constitution.md").Should().Contain("### VII. ");
        ReadRepoFile("specs", "ROADMAP.md").Should().Contain("## Goal").And.Contain("## Core architectural idea");
    }

    private static List<string> CallIds(string section)
        => CallRow().Matches(section).Select(m => m.Groups["id"].Value).ToList();

    private static HashSet<string> SectionNumbers(string doc)
        => [.. Heading().Matches(doc).Select(m => m.Groups["number"].Value)];

    /// <summary>
    /// The text from the heading that starts with <paramref name="heading"/> to the next heading of the same
    /// or a higher level.
    /// </summary>
    private static string Section(string doc, string heading)
    {
        var match = Regex.Match(doc, $"^{Regex.Escape(heading)}.*$", RegexOptions.Multiline);
        match.Success.Should().BeTrue($"the doc still has a heading starting '{heading}'");
        var level = heading.TakeWhile(c => c == '#').Count();
        var body = doc[(match.Index + match.Length)..];
        var next = Regex.Match(body, $@"^#{{1,{level}}} ", RegexOptions.Multiline);
        return match.Value + (next.Success ? body[..next.Index] : body);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path).ReplaceLineEndings("\n");
        }

        throw new InvalidOperationException($"Could not locate {Path.Combine(parts)} above the test output directory.");
    }

    [GeneratedRegex(@"^\| (?<id>M\d+) \| (?<name>[^|]+)\|", RegexOptions.Multiline)]
    private static partial Regex MeasureRow();

    [GeneratedRegex(@"^- \*\*(?<measure>M\d+ [^*]+)\*\*", RegexOptions.Multiline)]
    private static partial Regex PrimerMeasure();

    [GeneratedRegex(@"^\| (?<id>C\d+(?:\([iv]+\))?) \|", RegexOptions.Multiline)]
    private static partial Regex CallRow();

    [GeneratedRegex(@"^#{2,3} (?<number>\d+[a-z]?(?:\.\d+)?)[. ]", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?<doc>finance-framework|money-semantics) (?<sections>§[\d.a-z]+(?:, §[\d.a-z]+)*)")]
    private static partial Regex Citation();

    [GeneratedRegex(@"(?<=§)\d+[a-z]?(?:\.\d+)?")]
    private static partial Regex SectionList();
}

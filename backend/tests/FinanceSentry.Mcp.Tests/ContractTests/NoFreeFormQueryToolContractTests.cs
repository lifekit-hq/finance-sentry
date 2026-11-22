using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Mcp.Tests.ContractTests;

/// <summary>
/// Ledger's money answers come only from curated tools that follow docs/money-semantics.md. A free-form
/// query tool (ad-hoc SQL or analytics) can produce money figures that skip those rules, so none may be
/// registered, allow-listed or named in the Ledger persona (R6, issue #1508).
/// </summary>
public sealed partial class NoFreeFormQueryToolContractTests
{
    [GeneratedRegex(
        @"run_analytics_query|describe_query_schema|(run|execute|exec|raw|adhoc|ad_hoc)_?(sql|query)|(sql|analytics)_query",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FreeFormQueryName();

    private static IReadOnlyList<string> Offenders(IEnumerable<string> lines) =>
        lines.Where(line => FreeFormQueryName().IsMatch(line)).ToList();

    // Blocked: the detector itself must flag a free-form query tool, wherever the name shows up.
    [Theory]
    [InlineData("run_analytics_query")]
    [InlineData("describe_query_schema")]
    [InlineData("execute_sql")]
    [InlineData("Use run_analytics_query for exploratory questions.")]
    public void Guard_FlagsAFreeFormQueryTool(string line) =>
        Offenders([line]).Should().ContainSingle();

    // Allowed: the curated surface passes the same guard.
    [Fact]
    public void RegisteredTools_NameNoFreeFormQueryTool() =>
        Offenders(McpToolReflection.GetToolNames()).Should().BeEmpty(
            "every registered MCP tool must be a curated tool, not a free-form query path");

    [Fact]
    public void ToolAllowLists_NameNoFreeFormQueryTool()
    {
        var entries = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPath("backend", "src"), "appsettings*.json", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            CollectAllowListEntries(doc.RootElement, inAllowList: false, entries);
        }

        Offenders(entries).Should().BeEmpty("no allow-list entry may expose a free-form query tool");
    }

    [Fact]
    public void LedgerPersona_NamesNoFreeFormQueryTool()
    {
        var lines = Directory.EnumerateFiles(RepoPath("agent", "ledger"), "*.md", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .ToList();

        lines.Should().NotBeEmpty("the persona files must be found for this guard to mean anything");
        Offenders(lines).Should().BeEmpty("the Ledger persona must not point at a free-form query tool");
    }

    private static void CollectAllowListEntries(JsonElement element, bool inAllowList, List<string> entries)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectAllowListEntries(property.Value, inAllowList || property.Name.Contains("AllowList", StringComparison.OrdinalIgnoreCase), entries);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectAllowListEntries(item, inAllowList, entries);
                break;
            case JsonValueKind.String when inAllowList:
                entries.Add(element.GetString()!);
                break;
        }
    }

    private static string RepoPath(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "backend")))
                return candidate;
        }

        throw new DirectoryNotFoundException($"Repo path {string.Join('/', parts)} not found above {AppContext.BaseDirectory}");
    }
}

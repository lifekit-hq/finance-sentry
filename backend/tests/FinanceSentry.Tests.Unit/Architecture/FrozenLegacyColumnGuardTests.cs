namespace FinanceSentry.Tests.Unit.Architecture;

using System.Reflection;
using System.Text.RegularExpressions;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// R8 PR 4 (docs/finance-framework.md §5.6): the sleeve columns of <see cref="NetWorthSnapshot"/> are frozen.
/// A new economy is an <c>IWealthSource</c> with a <c>net_worth_snapshot_sources</c> row, so the next one must not
/// add a column here or a new reader of the legacy ones.
/// </summary>
public partial class FrozenLegacyColumnGuardTests
{
    private static readonly string[] FrozenColumns = ["BankingTotal", "BrokerageTotal", "CryptoTotal", "StaleSleeves"];

    // Every NetWorthSnapshot property as of the freeze. Adding one (typically a fourth sleeve) fails the test below.
    private static readonly string[] KnownProperties =
    [
        "Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency",
        "TakenAt", "StaleSleeves", "IsApproximate", "CashTotal", "BrokerageInvested", "CryptoInvested", "FxStatus",
    ];

    // The only files (under backend/src) that may touch a frozen column: the writer, its persistence, the PR 3
    // legacy-fallback adapter, and the three readers that predate the freeze. A new consumer reads per-source rows
    // through SnapshotSources instead; do not extend this list.
    private static readonly string[] AllowedFiles =
    [
        "Domain/NetWorthSnapshot.cs",
        "Domain/SnapshotSources.cs",
        "Application/Services/NetWorthSnapshotService.cs",
        "Infrastructure/Persistence/WealthDbContext.cs",
        "Infrastructure/Persistence/Repositories/NetWorthSnapshotRepository.cs",
        "Application/Queries/GetNetWorthHistoryQuery.cs",
        "Application/Services/BrokerageValueHistoryReader.cs",
        "Infrastructure/Jobs/NetWorthSplitBackfillService.cs",
    ];

    [Fact]
    public void NetWorthSnapshot_gains_no_column()
    {
        var actual = typeof(NetWorthSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        actual.Should().BeEquivalentTo(
            KnownProperties,
            "the sleeve columns are frozen: register the new economy as an IWealthSource and read its "
            + "net_worth_snapshot_sources row (docs/finance-framework.md, 'Adding a new economy')");
    }

    [Fact]
    public void Frozen_columns_carry_the_annotation_and_nothing_else_does()
    {
        var annotated = typeof(NetWorthSnapshot)
            .GetProperties()
            .Where(p => p.GetCustomAttribute<FrozenLegacyColumnAttribute>() is not null)
            .Select(p => p.Name);

        annotated.Should().BeEquivalentTo(FrozenColumns);
    }

    [Fact]
    public void Legacy_columns_are_read_only_by_the_writer_and_the_legacy_fallback()
    {
        var wealth = Path.Combine(FindBackendRoot(), "src", "FinanceSentry.Modules.Wealth");
        var anyColumn = new Regex(@"\.(" + string.Join('|', FrozenColumns) + @")\b");

        var violations = Directory.EnumerateFiles(Path.Combine(FindBackendRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => UsesSnapshotColumn(f, anyColumn))
            .Select(f => Path.GetRelativePath(wealth, f).Replace('\\', '/'))
            .Where(rel => !AllowedFiles.Contains(rel))
            .ToList();

        violations.Should().BeEmpty(
            "new consumers must read per-source rows through SnapshotSources, not the frozen sleeve columns");
    }

    private static bool UsesSnapshotColumn(string file, Regex anyColumn)
    {
        var text = File.ReadAllText(file);
        // Only files that deal in snapshots can read the entity's columns; other types (DTOs, snapshot DTOs
        // carrying a BankingTotal field of their own) are not the frozen entity columns.
        return (text.Contains(nameof(NetWorthSnapshot), StringComparison.Ordinal)
                || text.Contains("SnapshotSources", StringComparison.Ordinal))
            && anyColumn.IsMatch(text)
            && ReferencesEntity(text);
    }

    private static bool ReferencesEntity(string text)
        => Regex.IsMatch(text, @"\bNetWorthSnapshot\b(?!Dto|Source|Repository|Service|Job|Row)")
            || text.Contains("SnapshotSources", StringComparison.Ordinal);

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinanceSentry.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("FinanceSentry.sln not found above the test bin directory.");
    }
}

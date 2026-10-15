namespace FinanceSentry.Tests.Unit.Architecture;

using System.Reflection;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// R8 PR 4 (docs/finance-framework.md §5.6): the sleeve columns of <see cref="NetWorthSnapshot"/> are frozen.
/// A new economy is an <c>IWealthSource</c> with a <c>net_worth_snapshot_sources</c> row, so the next one must not
/// add a column here.
/// </summary>
public class FrozenLegacyColumnGuardTests
{
    private static readonly string[] FrozenColumns = ["BankingTotal", "BrokerageTotal", "CryptoTotal", "StaleSleeves"];

    // Every NetWorthSnapshot property as of the freeze. Adding one (typically a fourth sleeve) fails the test below.
    private static readonly string[] KnownProperties =
    [
        "Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency",
        "TakenAt", "StaleSleeves", "IsApproximate", "CashTotal", "BrokerageInvested", "CryptoInvested", "FxStatus",
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
}

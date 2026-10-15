namespace FinanceSentry.Modules.Wealth.Domain;

/// <summary>
/// Marks a <see cref="NetWorthSnapshot"/> column as frozen (R8 PR 4, docs/finance-framework.md §5.6): kept and
/// still written for history and for snapshots taken before per-source rows existed, never extended. A new
/// economy is an <c>IWealthSource</c> and gets a <c>net_worth_snapshot_sources</c> row; it does not get a column
/// here, and a new consumer reads per-source rows through <see cref="SnapshotSources"/>, not these.
/// Not <see cref="ObsoleteAttribute"/>: the writer and the legacy fallback still use them by design, and a
/// warning would only be suppressed. <c>FrozenLegacyColumnGuardTests</c> pins the column set and which columns carry this.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FrozenLegacyColumnAttribute(string replacement) : Attribute
{
    /// <summary>Where the figure lives now, e.g. <c>net_worth_snapshot_sources.total</c>.</summary>
    public string Replacement { get; } = replacement;
}

namespace FinanceSentry.Modules.Wealth.Domain;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// How trustworthy one source's figures were on a snapshot day, in the framework's quality vocabulary (§3.6):
/// COMPLETE, ESTIMATED, DEGRADED or UNAVAILABLE. Stored by name.
/// </summary>
public enum SourceHealth
{
    /// <summary>Read this run with live or published rates; nothing to caveat.</summary>
    Complete,

    /// <summary>Read this run, but a conversion used today's or a seed-table rate (<c>FX_TODAY_RATE</c>), or a value is an estimate (<c>ESTIMATED_VALUE</c>).</summary>
    Estimated,

    /// <summary>Read this run but unreliable (a 1:1 conversion), or not trusted this run so the snapshot carried the sleeve forward.</summary>
    Degraded,

    /// <summary>The read failed: the source contributed nothing and its amounts are null.</summary>
    Unavailable,
}

/// <summary>Typed reason codes a per-source row carries beyond the FX ones (<see cref="FinanceSentry.Core.Utils.FxReasons"/>).</summary>
public static class SourceReasons
{
    /// <summary>The source failed to read, or the snapshot carried its sleeve forward from an earlier day.</summary>
    public const string StaleSource = "STALE_SOURCE";

    /// <summary>The source read an estimated value (reliability L3: one the person entered), not a quoted price.</summary>
    public const string EstimatedValue = "ESTIMATED_VALUE";
}

/// <summary>
/// One registered <see cref="IWealthSource"/>'s contribution to one <see cref="NetWorthSnapshot"/> (framework §5.6,
/// step 2): written beside the snapshot's own columns by the snapshot job, in the same transaction. Amounts are in
/// the snapshot's <see cref="NetWorthSnapshot.Currency"/> and are null - never zero - when the source could not be
/// read. A row is as the source read this run: when the snapshot carried the sleeve forward, the snapshot's column
/// holds the carried figure and this row's <see cref="Health"/> says so. The child has no <c>UserId</c> of its
/// own; it belongs to whoever owns its snapshot.
/// </summary>
public sealed class NetWorthSnapshotSource
{
    public Guid Id { get; init; }
    public Guid SnapshotId { get; init; }

    /// <summary>The source's <see cref="IWealthSource.SourceId"/> ("banking", "brokerage", "crypto" for the original three).</summary>
    public string SourceId { get; init; } = string.Empty;

    public EconomyKind Kind { get; init; }

    /// <summary>Cash plus invested value as read; null = the source could not be read.</summary>
    public decimal? Total { get; init; }

    public decimal? Cash { get; init; }

    /// <summary>The non-cash positions' value; zero for a source with none (a bank), null as <see cref="Total"/>.</summary>
    public decimal? Invested { get; init; }

    public SourceHealth Health { get; init; }

    /// <summary>Typed reason codes (<c>FX_TODAY_RATE</c>, <c>FX_UNKNOWN_1TO1</c>, <c>STALE_SOURCE</c>, <c>ESTIMATED_VALUE</c>); empty = none.</summary>
    public string[] Reasons { get; init; } = [];
}

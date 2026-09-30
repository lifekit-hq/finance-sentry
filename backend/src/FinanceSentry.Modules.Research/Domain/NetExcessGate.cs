namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Why a <see cref="BenchmarkRelativeRecord.NetExcessReturnPct"/> is (or is not) populated. The net
/// figure nets trading cost and tax against a real position, so it exists only where that position's
/// cost basis is verified (fs-688); every other state leaves it null.
/// </summary>
public static class NetExcessGate
{
    /// <summary>The thesis ticker is a held brokerage position whose cost basis is verified.</summary>
    public const string Verified = "Verified";

    /// <summary>The thesis ticker is held, but its cost basis is not verified (or has no verification path).</summary>
    public const string Unverified = "Unverified";

    /// <summary>The thesis ticker is not held — there is no acquisition to net.</summary>
    public const string NotHeld = "NotHeld";

    /// <summary>Aggregate row: at least one covered constituent is not <see cref="Verified"/>.</summary>
    public const string Incomplete = "Incomplete";
}

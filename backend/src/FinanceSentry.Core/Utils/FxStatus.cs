namespace FinanceSentry.Core.Utils;

/// <summary>
/// How a currency conversion's rate was obtained, ordered from best to worst so the worst status
/// of a figure's conversions is simply the maximum (<see cref="FxStatusExtensions.Worst"/>).
/// </summary>
public enum FxStatus
{
    /// <summary>The rate for the valuation date: a live rate for a present-day conversion, or the rate a feed published for that date.</summary>
    Exact = 0,

    /// <summary>No rate was published for the date (weekend, holiday): the closest earlier published rate was used.</summary>
    NearestPublished = 1,

    /// <summary>A past date was converted at today's rate because no feed published one.</summary>
    TodayRate = 2,

    /// <summary>The rate came from the hard-coded offline seed table, not a live feed.</summary>
    SeedTable = 3,

    /// <summary>The currency has no rate at all and the amount passed through 1:1.</summary>
    Unknown1To1 = 4,
}

/// <summary>A converted amount plus how its rate was obtained.</summary>
public readonly record struct ConvertResult(decimal Amount, FxStatus Status);

/// <summary>Typed reason codes a figure carries when an FX substitution touched it (framework R4).</summary>
public static class FxReasons
{
    public const string TodayRate = "FX_TODAY_RATE";
    public const string Unknown1To1 = "FX_UNKNOWN_1TO1";
}

public static class FxStatusExtensions
{
    /// <summary>The worse of two statuses.</summary>
    public static FxStatus Worst(this FxStatus a, FxStatus b) => a >= b ? a : b;

    /// <summary>
    /// The reason code a status maps to, or null when the figure needs none. A seed-table rate is a
    /// stand-in for a live rate, so it reads as <see cref="FxReasons.TodayRate"/>; an unknown currency
    /// passed through 1:1 reads as <see cref="FxReasons.Unknown1To1"/>.
    /// </summary>
    public static string? ToReason(this FxStatus status) => status switch
    {
        FxStatus.TodayRate or FxStatus.SeedTable => FxReasons.TodayRate,
        FxStatus.Unknown1To1 => FxReasons.Unknown1To1,
        _ => null,
    };

    /// <summary>The reason codes for a status: empty when none applies, otherwise the one reason.</summary>
    public static IReadOnlyList<string> ToReasons(this FxStatus status) =>
        status.ToReason() is { } reason ? [reason] : [];
}

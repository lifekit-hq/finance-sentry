using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.Radar.Domain;

/// <summary>Why a holdings-based return is not a number. A reason is returned instead of a figure, never 0.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HoldingsReturnUnavailableReason>))]
public enum HoldingsReturnUnavailableReason
{
    /// <summary>Fewer than two holdings captures fall inside the window, so there is no step to chain.</summary>
    InsufficientCaptures,

    /// <summary>The first or last capture is further from the window's edge than the tolerance allows.</summary>
    WindowNotCovered,

    /// <summary>A position held at two consecutive captures had no price at one of them.</summary>
    MissingPrice,

    /// <summary>No capture in the window held a priced position, so there is nothing to revalue.</summary>
    NoInvestedBase,
}

/// <summary>
/// A holdings-based return over a window: a number, or the reason there is none. Exactly one of
/// <see cref="Value"/> and <see cref="Reason"/> is set.
/// </summary>
public sealed record HoldingsBasedReturnResult(
    decimal? Value,
    HoldingsReturnUnavailableReason? Reason,
    IReadOnlyList<string> MissingPriceSymbols)
{
    public static HoldingsBasedReturnResult Of(decimal value) => new(value, null, []);

    public static HoldingsBasedReturnResult Unavailable(
        HoldingsReturnUnavailableReason reason, IReadOnlyList<string>? missingPriceSymbols = null) =>
        new(null, reason, missingPriceSymbols ?? []);
}

namespace FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary>
/// Read-port: the user's brokerage holdings as captured over time (symbol, quantity, USD value per
/// capture). Lives in Radar's domain so the holdings-based return can be measured without a
/// compile-time dependency on the module that persists the captures.
/// </summary>
public interface IHoldingsHistorySource
{
    /// <summary>
    /// Brokerage captures taken from <paramref name="from"/> (start of that UTC day) onwards, oldest first.
    /// Every position held at a capture is listed, including one whose value could not be priced
    /// (<see cref="CapturedHolding.UsdValue"/> not above zero). Empty when nothing was captured.
    /// </summary>
    Task<IReadOnlyList<HoldingsCapture>> GetBrokerageCapturesAsync(
        Guid userId, DateOnly from, CancellationToken ct = default);
}

/// <summary>One point-in-time capture of the brokerage book's positions.</summary>
public sealed record HoldingsCapture(DateTimeOffset CapturedAt, IReadOnlyList<CapturedHolding> Holdings);

/// <summary>A position at a capture. <c>UsdValue</c> of zero or less means the position had no price then.</summary>
public sealed record CapturedHolding(string Symbol, decimal Quantity, decimal UsdValue);

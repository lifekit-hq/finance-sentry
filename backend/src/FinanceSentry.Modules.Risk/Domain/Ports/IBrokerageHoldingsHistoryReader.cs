namespace FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Published read port: the brokerage sleeve's holding captures (symbol, quantity and USD value per
/// position per risk-check run) for cross-module readers such as Radar's holdings-based return.
/// Implemented inside Risk; the FinanceSentry.Integration adapters reach Risk only through this interface.
/// </summary>
public interface IBrokerageHoldingsHistoryReader
{
    /// <summary>
    /// Captures taken at or after <paramref name="since"/>, oldest first, one per distinct capture
    /// instant. Reads unscoped: the weekly brief job runs with no person in scope.
    /// </summary>
    Task<IReadOnlyList<BrokerageHoldingsCapture>> ListSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default);
}

public sealed record BrokerageHoldingsCapture(
    DateTimeOffset CapturedAt,
    IReadOnlyList<BrokerageHoldingPosition> Positions);

public sealed record BrokerageHoldingPosition(string Symbol, decimal Quantity, decimal UsdValue);

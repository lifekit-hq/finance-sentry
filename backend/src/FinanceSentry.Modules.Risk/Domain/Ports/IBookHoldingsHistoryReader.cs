namespace FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Published read port: every holding capture across both sleeves (brokerage and crypto) with the sleeve each
/// position sat in, for cross-module readers that chart concentration over time. Implemented inside Risk; the
/// FinanceSentry.Integration adapters reach Risk only through this interface.
/// </summary>
public interface IBookHoldingsHistoryReader
{
    /// <summary>Captures taken at or after <paramref name="since"/>, oldest first, one per distinct capture instant. Reads unscoped: it can run with no person in scope.</summary>
    Task<IReadOnlyList<BookHoldingsCapture>> ListSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default);
}

public sealed record BookHoldingsCapture(DateTimeOffset CapturedAt, IReadOnlyList<BookHoldingPosition> Positions);

public sealed record BookHoldingPosition(string Symbol, string Sleeve, decimal UsdValue);

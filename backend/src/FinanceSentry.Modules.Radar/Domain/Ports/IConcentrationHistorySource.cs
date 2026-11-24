namespace FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary>
/// Read-port: what the concentration-and-allocation history takes from other modules - the holding captures of both
/// sleeves (Risk), the invested book's cash against its total per snapshot day (Wealth) and the IPS allocation
/// targets (Research). Lives in Radar's domain; the Integration layer implements it over the published ports, so
/// Radar references none of those modules.
/// </summary>
public interface IConcentrationHistorySource
{
    Task<ConcentrationHistoryInputs> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}

/// <param name="Captures">Holding captures, oldest first.</param>
/// <param name="Cash">One entry per snapshot day with a measured cash/invested split.</param>
/// <param name="Targets">The IPS targets as fractions per canonical asset class; empty when no IPS is set.</param>
public sealed record ConcentrationHistoryInputs(
    IReadOnlyList<ConcentrationCapture> Captures,
    IReadOnlyList<ConcentrationCash> Cash,
    IReadOnlyList<ConcentrationTarget> Targets);

public sealed record ConcentrationCapture(DateTimeOffset CapturedAt, IReadOnlyList<ConcentrationPosition> Positions);

/// <param name="IsCrypto">Whether the position sat in the crypto sleeve.</param>
public sealed record ConcentrationPosition(string Symbol, bool IsCrypto, decimal UsdValue);

public sealed record ConcentrationCash(DateOnly Date, decimal Cash, decimal Total);

/// <param name="AssetClass">A canonical class (<c>AssetClassNormalizer</c>).</param>
/// <param name="TargetFraction">The target weight as a fraction.</param>
public sealed record ConcentrationTarget(string AssetClass, decimal TargetFraction);

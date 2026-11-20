namespace FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary>
/// Read-port: what the risk panel takes from other modules - the invested positions' weights, the stored
/// single-position flag and the owner's drawdown tolerance. Lives in Radar's domain; the Integration layer
/// implements it over the Risk and Research published ports, so Radar references neither.
/// </summary>
public interface IRiskPictureInputsSource
{
    Task<RiskPictureInputs> ReadAsync(Guid userId, CancellationToken ct = default);
}

/// <param name="Positions">One entry per asset; <see cref="RiskPosition.Weight"/> is its fraction of the whole book
/// (cash included), the denominator the risk rule's position cap is compared against.</param>
/// <param name="PositionFlag">The stored risk rule's single-position weight limit as a fraction, null when no rule set exists or it is unset.</param>
/// <param name="DrawdownTolerance">The IPS maximum drawdown tolerance as a fraction, null when not set.</param>
public sealed record RiskPictureInputs(
    IReadOnlyList<RiskPosition> Positions,
    decimal? PositionFlag,
    decimal? DrawdownTolerance);

public sealed record RiskPosition(string Symbol, string AssetClass, decimal Weight);

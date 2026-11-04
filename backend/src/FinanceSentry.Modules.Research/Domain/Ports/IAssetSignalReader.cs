namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Cross-module port (feature 421). Provides the Asset Dossier with recent Radar signals for a
/// specific symbol. The concrete adapter lives in FinanceSentry.Integration so Modules.Research
/// never depends on Modules.Radar directly.
/// </summary>
public interface IAssetSignalReader
{
    /// <summary>
    /// Returns the most recent Radar signals whose <c>Subject</c> equals <paramref name="symbol"/>,
    /// newest first, capped at <paramref name="limit"/>. Returns an empty list when no signals exist.
    /// </summary>
    Task<IReadOnlyList<DossierSignalItem>> GetRecentAsync(
        string symbol, int limit, CancellationToken ct = default);

    /// <summary>
    /// Returns the Radar signals whose <c>Subject</c> equals <paramref name="symbol"/> dated on or after
    /// <paramref name="since"/>, newest first, capped at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<DossierSignalItem>> GetSinceAsync(
        string symbol, DateOnly since, int limit, CancellationToken ct = default);
}

/// <summary>A single Radar signal surfaced in the Asset Dossier.</summary>
public sealed record DossierSignalItem(
    DateTimeOffset Timestamp,
    string Scanner,
    string SignalType,
    string Severity,
    IReadOnlyDictionary<string, object> Payload);

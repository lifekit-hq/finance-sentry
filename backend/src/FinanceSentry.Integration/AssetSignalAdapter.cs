namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Feature 421 - implements <see cref="IAssetSignalReader"/> over the Radar module's published
/// <see cref="ISubjectSignalReader"/> port (#673). Lives in Integration so Modules.Research never
/// references Modules.Radar directly.
/// </summary>
public sealed class AssetSignalAdapter(ISubjectSignalReader signals) : IAssetSignalReader
{
    private static readonly int LookbackDays = 30;

    public async Task<IReadOnlyList<DossierSignalItem>> GetRecentAsync(
        string symbol, int limit, CancellationToken ct = default)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-LookbackDays));

        var recent = await signals.ListSinceAsync(symbol, since, ct);

        return recent
            .OrderByDescending(s => s.Timestamp)
            .Take(limit)
            .Select(s => new DossierSignalItem(
                Timestamp: s.Timestamp,
                Scanner: s.Scanner,
                SignalType: s.SignalType,
                Severity: s.Severity,
                Payload: s.Payload))
            .ToList();
    }
}

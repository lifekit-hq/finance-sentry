namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Domain;

/// <summary>
/// Exports the person's own radar signals. A signal with no <c>UserId</c> is a global one every person reads, not theirs.
/// </summary>
internal sealed class RadarUserDataExporter(RadarDbContext db) : IUserDataExporter
{
    public string Module => "Radar";

    public Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
        => OwnedRowExporter.WriteRowsAsync<RadarSignal>(db, s => s.UserId == userId, writer, ct);
}

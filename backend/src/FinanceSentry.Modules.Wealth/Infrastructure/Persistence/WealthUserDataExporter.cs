namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Migrations;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Exports a person's Wealth data over the rows <see cref="WealthUserDataEraser"/> deletes: the owned-entity sweep, plus
/// M005's backup of the net-worth snapshots it corrected. That backup is raw SQL outside the EF model and holds a full
/// copy of each snapshot by <c>UserId</c>, so it is the person's data like the snapshots themselves.
/// </summary>
internal sealed class WealthUserDataExporter(WealthDbContext db) : IUserDataExporter
{
    public string Module => "Wealth";

    public async Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
    {
        await OwnedRowExporter.ExportAsync(db, userId, writer, ct);

        writer.WritePropertyName(M005_CorrectInzhurNetWorthHistory.BackupTable);
        writer.WriteStartArray();
        var rows = db.Database.SqlQueryRaw<string>(
            $"SELECT to_jsonb(b)::text AS \"Value\" FROM {M005_CorrectInzhurNetWorthHistory.BackupTable} b WHERE b.\"UserId\" = {{0}}",
            userId);
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct))
            writer.WriteRawValue(row, skipInputValidation: true);
        writer.WriteEndArray();
        await writer.FlushAsync(ct);
    }
}

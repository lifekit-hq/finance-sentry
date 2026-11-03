namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;

/// <summary>Exports the person's detected subscriptions, keyed by the person's id in its string form.</summary>
internal sealed class SubscriptionsUserDataExporter(SubscriptionsDbContext db) : IUserDataExporter
{
    public string Module => "Subscriptions";

    public Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
    {
        var key = userId.ToString();
        return OwnedRowExporter.WriteRowsAsync<Domain.DetectedSubscription>(db, s => s.UserId == key, writer, ct);
    }
}

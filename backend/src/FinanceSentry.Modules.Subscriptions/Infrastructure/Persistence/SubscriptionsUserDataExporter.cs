namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;

/// <summary>Exports the person's detected subscriptions and their price changes, keyed by the person's id in its string form.</summary>
internal sealed class SubscriptionsUserDataExporter(SubscriptionsDbContext db) : IUserDataExporter
{
    public string Module => "Subscriptions";

    public async Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
    {
        var key = userId.ToString();
        await OwnedRowExporter.WriteRowsAsync<Domain.DetectedSubscription>(db, s => s.UserId == key, writer, ct);
        await OwnedRowExporter.WriteRowsAsync<Domain.SubscriptionPriceChange>(db, c => c.UserId == key, writer, ct);
    }
}

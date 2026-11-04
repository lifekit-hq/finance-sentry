using System.Text.Json;
using FinanceSentry.Core.Interfaces;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

/// <summary>
/// Writes the signed-in person's data export: one JSON document, every module's rows for that person under the module's
/// name, produced by the <see cref="IUserDataExporter"/> each module registers beside its
/// <see cref="IUserDataEraser"/>. The person is the only argument, taken by the caller from the access token; nothing in
/// the request names whose data to export.
/// </summary>
public sealed class AccountDataExportWriter(IEnumerable<IUserDataExporter> exporters, TimeProvider clock)
{
    /// <summary>Identifies the document layout, so a later change to it can be told from this one.</summary>
    public const string Format = "finance-sentry-export/1";

    public async Task WriteAsync(Guid userId, Stream output, CancellationToken ct = default)
    {
        await using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("format", Format);
        writer.WriteString("exportedAt", clock.GetUtcNow());
        writer.WriteString("userId", userId);
        writer.WriteStartObject("modules");

        // A module may register more than one exporter (a context plus its extras); their tables share one object.
        foreach (var module in exporters.GroupBy(e => e.Module, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(module.Key);
            foreach (var exporter in module)
                await exporter.ExportAsync(userId, writer, ct);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(ct);
    }
}

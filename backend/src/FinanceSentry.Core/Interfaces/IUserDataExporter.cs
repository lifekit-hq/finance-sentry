using System.Text.Json;

namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Writes every row one module holds for a person, for the person's own data export. The mirror of
/// <see cref="IUserDataEraser"/>: each module that registers an eraser registers one exporter over the same rows, so the
/// export covers exactly what closing the account deletes (a parity test pins that the two lists match). Opts out of
/// every query filter, as the eraser does, and keeps its own <c>UserId</c> predicate: that predicate is the only scope,
/// so another person's row can never reach the file. Never writes a stored secret (provider keys, tokens, push
/// endpoints): the export is the person's data, not the means to act as them.
/// </summary>
public interface IUserDataExporter
{
    /// <summary>The module's name, the same one its eraser records; the export groups rows under it.</summary>
    string Module { get; }

    /// <summary>
    /// Writes the module's rows as properties of the object the caller has already opened: one property per table,
    /// holding an array of that table's rows for <paramref name="userId"/>.
    /// </summary>
    Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default);
}

namespace FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary>
/// Published read port (#673): the Radar signals recorded against one subject (a ticker) since a
/// date, for cross-module readers such as Research's Asset Dossier. Implemented inside Radar; the
/// FinanceSentry.Integration adapter reaches Radar only through this interface.
/// </summary>
public interface ISubjectSignalReader
{
    /// <summary>
    /// Every signal whose <c>Subject</c> equals <paramref name="subject"/> recorded from the start of
    /// <paramref name="since"/> (UTC), across every scanner and signal type. Empty when none exist.
    /// </summary>
    Task<IReadOnlyList<SubjectSignal>> ListSinceAsync(
        string subject, DateOnly since, CancellationToken ct = default);
}

/// <summary>One Radar signal as a cross-module reader sees it.</summary>
public sealed record SubjectSignal(
    DateTimeOffset Timestamp,
    string Scanner,
    string SignalType,
    string Severity,
    IReadOnlyDictionary<string, object> Payload);

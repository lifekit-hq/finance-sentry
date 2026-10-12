namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Cross-module read contract for alerts that are pushed straight to a phone instead of through a companion event
/// (the deduped analyst rating-change alert). Implemented by the Alerts module; read by the Companion push sender.
/// </summary>
public interface IPushAlertReader
{
    /// <summary>Non-dismissed alerts of one of <paramref name="types"/> created at or after <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<MaterialAlertRecord>> ListOfTypesSinceAsync(
        IReadOnlyCollection<string> types, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>The still-open (not dismissed) alerts among <paramref name="alertIds"/>, by id.</summary>
    Task<IReadOnlyDictionary<Guid, MaterialAlertRecord>> GetOpenAsync(
        IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default);
}

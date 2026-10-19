namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// One raw event as an exchange or other source reports it (framework §5.2). The engine classifies it into the
/// flow classes per scope at measure time; the adapter never decides "external" or "internal", so every string
/// here is the provider's own wording.
/// </summary>
/// <param name="OccurredAtUtc">
/// When the provider says it happened, in UTC. The time of day is needed: the 24 h transfer-match window and
/// in-transit detection compare instants, not dates. A provider that reports only a date gives midnight UTC.
/// </param>
/// <param name="ProviderType">The provider's own event type string, kept raw.</param>
/// <param name="Amount">Signed, in <paramref name="Unit"/>; the fee is not netted out of it.</param>
/// <param name="Unit">The native unit (ticker or currency code), not a USD value.</param>
/// <param name="CounterpartyRef">The other side as the provider names it (address, account, venue), if any.</param>
/// <param name="TxRef">
/// The provider's transaction reference as reported, e.g. the on-chain hash. Stays raw: it is normalized (case,
/// prefix) only when two events are matched, so the stored value is never rewritten.
/// </param>
/// <param name="Network">The chain or rail the movement used (e.g. "ETH", "TRX"), when the venue reports it.</param>
/// <param name="Fee">The fee the venue charged for the event, in <paramref name="FeeUnit"/>; null when not reported.</param>
/// <param name="FeeUnit">The unit of <paramref name="Fee"/>, which can differ from <paramref name="Unit"/> (e.g. BNB).</param>
/// <param name="Status">The provider's own status string (pending, completed, failed, ...), kept raw.</param>
public sealed record SourceEvent(
    DateTimeOffset OccurredAtUtc,
    string ProviderType,
    decimal Amount,
    string Unit,
    string? CounterpartyRef,
    string? TxRef,
    string? Network,
    decimal? Fee,
    string? FeeUnit,
    string? Status);

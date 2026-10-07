namespace FinanceSentry.Core.Connections;

/// <summary>
/// Shadow mode of the connection-health policy (Option B, slice S1): a sync path reports each attempt here,
/// the policy's verdict is logged and the new health stored through <c>persist</c>, and nothing else
/// happens. No alert reads the outcome yet, so the sync path's own alerting stays exactly as it was.
/// Best-effort: a failure here is logged and swallowed, never thrown into the sync.
/// </summary>
public interface IConnectionHealthShadow
{
    /// <returns>The evaluation, or null if it could not be recorded.</returns>
    Task<ConnectionHealthEvaluation?> RecordSuccessAsync(
        ConnectionHealthSubject subject,
        ConnectionHealth current,
        Func<ConnectionHealth, CancellationToken, Task> persist,
        CancellationToken ct = default);

    /// <returns>The evaluation, or null if it could not be recorded.</returns>
    Task<ConnectionHealthEvaluation?> RecordFailureAsync(
        ConnectionHealthSubject subject,
        ConnectionHealth current,
        ProviderFailure failure,
        Func<ConnectionHealth, CancellationToken, Task> persist,
        CancellationToken ct = default);
}

/// <summary>Which connection an attempt belongs to, for the log line.</summary>
/// <param name="Provider">The provider, as the sync path names it (<c>monobank</c>, <c>ibkr</c>, …).</param>
/// <param name="Kind">The row that carries the health (<c>BankAccount</c>, <c>IBKRCredential</c>, …).</param>
/// <param name="Id">That row's id.</param>
/// <param name="UserId">Its owner.</param>
public sealed record ConnectionHealthSubject(string Provider, string Kind, Guid Id, Guid UserId);

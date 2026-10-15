namespace FinanceSentry.Core.Connections;

/// <summary>
/// One sync module's probes for the connection-health probe job (report §10 Option B, slice S4): which of its
/// connections may need one, and the provider's cheap authenticated call on one of them. The job decides which are due
/// (<see cref="ConnectionHealthPolicy.ProbeDue"/>) and that only an active provider is called; the module makes the call
/// and records its result through the policy - a success as any success, a failure through
/// <see cref="ConnectionHealthPolicy.RecordProbeFailure"/> - and raises or resolves its alert as its sync would.
/// </summary>
public interface IConnectionHealthProbe
{
    /// <summary>
    /// Every user's connections whose health is suspect or Dormant (read past the owner filter: the job has no principal).
    /// </summary>
    Task<IReadOnlyList<ConnectionHealthProbeTarget>> ListCandidatesAsync(CancellationToken ct = default);

    /// <summary>Probes one connection and records the result. Best-effort: never throws for the provider's answer.</summary>
    Task ProbeAsync(ConnectionHealthProbeTarget target, ConnectionHealthProbeReason reason, CancellationToken ct = default);
}

/// <summary>A connection the probe job may probe.</summary>
/// <param name="Subject">The connection row, as the health log names it.</param>
/// <param name="Health">Its stored health.</param>
/// <param name="ProbesDormant">
/// Whether the provider's probe is fit to heal a Dormant connection. False for a probe with a side effect worth a call
/// only on a suspect credential (TrueLayer's token refresh rotates the refresh token): such a Dormant connection is left
/// to its slowed-cadence sync.
/// </param>
public sealed record ConnectionHealthProbeTarget(ConnectionHealthSubject Subject, ConnectionHealth Health, bool ProbesDormant = true);

namespace FinanceSentry.Core.Connections;

/// <summary>Why the probe job probes a connection (<see cref="ConnectionHealthPolicy.ProbeDue"/>).</summary>
public enum ConnectionHealthProbeReason
{
    /// <summary>A suspect credential failure stands unconfirmed: the probe confirms or clears it.</summary>
    Suspect,

    /// <summary>The connection is Dormant and its slowed cadence is due: a success heals it.</summary>
    Dormant,
}

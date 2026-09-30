namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Sends the outbound "wake" to the agent runtime for a realtime event (feature 031). Carries only
/// ids/refs — no secrets or full detail (FR-016). The agent resolves specifics via MCP.
/// </summary>
public interface IAgentWakeDispatcher
{
    Task<WakeResult> WakeAsync(CompanionEvent evt, CancellationToken ct = default);

    /// <summary>Wakes the agent to compose the daily digest for a user (feature 031, US3).</summary>
    Task<WakeResult> WakeDigestAsync(Guid userId, int heldCount, CancellationToken ct = default);
}

/// <summary>Outcome of a wake attempt.</summary>
public enum WakeResult
{
    /// <summary>No trigger URL configured — leave the event pending for the agent to pull.</summary>
    NotConfigured,

    /// <summary>Successfully posted to the agent runtime.</summary>
    Sent,

    /// <summary>The event's user is not the owner account — nothing is sent and the event stays for the pull path.</summary>
    Skipped,

    /// <summary>Post failed — retry on a later tick.</summary>
    Failed,
}

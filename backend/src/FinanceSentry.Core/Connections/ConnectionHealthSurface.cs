namespace FinanceSentry.Core.Connections;

/// <summary>
/// What a person is shown of a connection's health (Option B, slice S5): the app's sync label, the MCP health read and
/// Companion all read it from here, so they cannot drift apart. Only a provider in
/// <see cref="ConnectionHealthOptions.ActiveProviders"/> is shown its health; a shadow provider keeps whatever its own
/// path shows (<see cref="StatusFor(string, ConnectionHealthState?, ConnectionHealthOptions)"/> answers null).
/// <list type="bullet">
/// <item>Degraded and Failing read <see cref="Retrying"/>: the policy keeps trying and there is nothing to do.</item>
/// <item>ActionRequired reads <see cref="ReconnectNeeded"/>: only the person can fix it.</item>
/// <item>Dormant reads <see cref="Dormant"/>: failing for long enough that the account may be closed.</item>
/// <item>Healthy has no label of its own: the path's freshness ("synced", "stale") still says how current it is.</item>
/// </list>
/// </summary>
public static class ConnectionHealthSurface
{
    public const string Retrying = "retrying";

    public const string ReconnectNeeded = "reconnect_needed";

    public const string Dormant = "dormant";

    /// <summary>The label of <paramref name="state"/>, or null for Healthy.</summary>
    public static string? StatusFor(ConnectionHealthState state) => state switch
    {
        ConnectionHealthState.Degraded or ConnectionHealthState.Failing => Retrying,
        ConnectionHealthState.ActionRequired => ReconnectNeeded,
        ConnectionHealthState.Dormant => Dormant,
        _ => null,
    };

    /// <summary>
    /// The label <paramref name="provider"/>'s connection shows, or null when the path keeps its own status: the provider
    /// is in shadow, its health is unknown, or it is Healthy.
    /// </summary>
    public static string? StatusFor(string provider, ConnectionHealthState? state, ConnectionHealthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return state is { } known && options.IsActive(provider) ? StatusFor(known) : null;
    }

    /// <summary>
    /// The state one account shows: its connection's when the connection is not Healthy (the connection carries the
    /// health that alerts, so a lost token is every account's problem), else the account's own sub-state (one account
    /// failing while its connection works).
    /// </summary>
    public static ConnectionHealthState? Effective(ConnectionHealthState? connection, ConnectionHealthState? account) =>
        connection is { } shared && shared != ConnectionHealthState.Healthy ? shared : account ?? connection;

    /// <summary>
    /// The state a group of connections or accounts shows (an institution, a provider): the most urgent one, where a
    /// Dormant member is quiet - it counts only when every member is Dormant, so one closed account does not mark its
    /// working siblings. Null for an empty group.
    /// </summary>
    public static ConnectionHealthState? Worst(IEnumerable<ConnectionHealthState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        ConnectionHealthState? worst = null;
        var any = false;
        var allDormant = true;
        foreach (var state in states)
        {
            any = true;
            if (state == ConnectionHealthState.Dormant)
                continue;

            allDormant = false;
            if (worst is null || Urgency(state) > Urgency(worst.Value))
                worst = state;
        }

        return !any ? null : allDormant ? ConnectionHealthState.Dormant : worst;
    }

    private static int Urgency(ConnectionHealthState state) => state switch
    {
        ConnectionHealthState.ActionRequired => 4,
        ConnectionHealthState.Failing => 3,
        ConnectionHealthState.Degraded => 2,
        ConnectionHealthState.Dormant => 1,
        _ => 0,
    };
}

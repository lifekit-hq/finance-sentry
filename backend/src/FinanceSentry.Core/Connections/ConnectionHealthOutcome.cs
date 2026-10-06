namespace FinanceSentry.Core.Connections;

/// <summary>What the caller should do after the policy recorded an attempt. Fires once, on the transition.</summary>
public enum ConnectionHealthOutcome
{
    /// <summary>Nothing to tell anyone.</summary>
    None,

    /// <summary>The connection just became <see cref="ConnectionHealthState.Failing"/>: tell the user, nothing for them to do.</summary>
    NotifyFailing,

    /// <summary>The connection just became <see cref="ConnectionHealthState.ActionRequired"/>: ask the user to reconnect, even in Quiet mode.</summary>
    NotifyActionRequired,

    /// <summary>The connection just became <see cref="ConnectionHealthState.Dormant"/>: ask the user once whether to hide it.</summary>
    GoDormant,

    /// <summary>A success after the user was told about a failure: resolve what they were told.</summary>
    Resolve,
}

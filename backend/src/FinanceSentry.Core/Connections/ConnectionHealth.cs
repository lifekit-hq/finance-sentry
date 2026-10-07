namespace FinanceSentry.Core.Connections;

/// <summary>
/// The durable health record of one provider connection (report §6–§7, Option B). Each owning module
/// embeds it on its own credential or connection row, so there is no shared table; only
/// <see cref="ConnectionHealthPolicy"/> moves it. Immutable: the policy returns a new value.
/// </summary>
public sealed record ConnectionHealth
{
    /// <summary>Column width of <see cref="LastFailureCode"/>.</summary>
    public const int FailureCodeMaxLength = 100;

    public ConnectionHealthState State { get; init; } = ConnectionHealthState.Healthy;

    /// <summary>Failures since the last success. Internal failures are not counted.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>The first failure of the current streak; null while healthy.</summary>
    public DateTimeOffset? FirstFailureAt { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public FailureClass? LastFailureClass { get; init; }

    public string? LastFailureCode { get; init; }

    /// <summary>When the first unconfirmed suspect credential failure of the current streak was seen.</summary>
    public DateTimeOffset? SuspectSince { get; init; }

    /// <summary>When <see cref="State"/> last changed; null until the first change.</summary>
    public DateTimeOffset? StateChangedAt { get; init; }
}

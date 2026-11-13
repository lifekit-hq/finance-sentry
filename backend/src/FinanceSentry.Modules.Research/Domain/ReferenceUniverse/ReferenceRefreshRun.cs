namespace FinanceSentry.Modules.Research.Domain.ReferenceUniverse;

/// <summary>
/// The log row of one refresh attempt of one dataset. A failed refresh writes one with <see cref="Error"/>
/// set and leaves the previous members in place, so a stale list is always explained by a row, never silent.
/// </summary>
public sealed class ReferenceRefreshRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>One of <see cref="ReferenceSources"/>.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Members the source now holds after a successful run; 0 for a failed one.</summary>
    public int MemberCount { get; set; }

    public string? Error { get; set; }
}

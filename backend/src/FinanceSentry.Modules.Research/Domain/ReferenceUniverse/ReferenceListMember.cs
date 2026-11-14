namespace FinanceSentry.Modules.Research.Domain.ReferenceUniverse;

/// <summary>
/// One ticker's membership of one reference list (S&amp;P 500, Nasdaq-100, a sector, an industry, a theme).
/// Shared market reference data, not per-user: no <c>UserId</c>, so the owner query filter does not apply.
/// A ticker carries its lists as the set of rows that name it.
/// </summary>
public sealed class ReferenceListMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>List identity, see <see cref="ReferenceListKeys"/> (<c>sp500</c>, <c>sector:Energy</c>, <c>theme:memory</c>).</summary>
    public string ListKey { get; set; } = string.Empty;

    /// <summary>Yahoo-form upper-case ticker (share classes use a dash: <c>BRK-B</c>).</summary>
    public string Ticker { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>The dataset that supplied the row (<see cref="ReferenceSources"/>); the unit a refresh replaces.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

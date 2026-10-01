namespace FinanceSentry.Core.Auth;

/// <summary>
/// The person the current unit of work acts for. Per-user DbContexts read it to scope every query to that
/// person's rows through the <see cref="OwnerQueryFilter"/>. Background jobs and sweeps run with no person
/// (<see cref="UserId"/> is null), which makes the filter match nothing - a job that legitimately reads
/// across users opts out explicitly with <c>IgnoreQueryFilters([OwnerQueryFilter.Name])</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The acting user's id, or null when no authenticated person is in scope.</summary>
    Guid? UserId { get; }
}

/// <summary>An <see cref="ICurrentUser"/> with no person in scope - for design-time tooling and job-shaped contexts.</summary>
public sealed class NoCurrentUser : ICurrentUser
{
    public static readonly NoCurrentUser Instance = new();

    private NoCurrentUser()
    {
    }

    public Guid? UserId => null;
}

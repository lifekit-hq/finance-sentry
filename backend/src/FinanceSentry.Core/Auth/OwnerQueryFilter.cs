namespace FinanceSentry.Core.Auth;

/// <summary>
/// The named EF Core query filter that scopes a per-user entity to the rows of <see cref="ICurrentUser.UserId"/>.
/// It is about row ownership, not the <see cref="AuthRoles.Owner"/> role: every person - owner or member - sees
/// only their own rows. Declared with <c>HasQueryFilter(OwnerQueryFilter.Name, e =&gt; e.UserId == CurrentUserId)</c>
/// and bypassed only by cross-user jobs via <c>IgnoreQueryFilters([OwnerQueryFilter.Name])</c>.
/// </summary>
public static class OwnerQueryFilter
{
    public const string Name = "Owner";
}

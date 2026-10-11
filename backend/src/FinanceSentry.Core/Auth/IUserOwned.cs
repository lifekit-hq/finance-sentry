namespace FinanceSentry.Core.Auth;

/// <summary>
/// An entity whose rows belong to one person. A context derived from <c>OwnerScopedDbContext</c> scopes every
/// implementer to <see cref="ICurrentUser.UserId"/> through the <see cref="OwnerQueryFilter"/>, so a new per-user
/// entity is filtered by declaring this interface - there is no filter to remember.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; }
}

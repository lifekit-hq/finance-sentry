namespace FinanceSentry.Modules.Research.Tests;

using FinanceSentry.Core.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> pinned to one person for a test's DbContext. Null stands for a background job:
/// no person in scope, so the Owner query filter matches nothing unless the code under test opts out.
/// </summary>
public sealed class FixedCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; } = userId;
}

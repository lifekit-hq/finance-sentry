namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;

/// <summary><see cref="ICurrentUser"/> pinned to one person for a test's DbContext; null is no person in scope.</summary>
internal sealed class FixedCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; } = userId;
}

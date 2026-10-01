namespace FinanceSentry.Tests.Unit.BankSync;

using FinanceSentry.Core.Auth;

/// <summary><see cref="ICurrentUser"/> pinned to one person for a test's DbContext.</summary>
public sealed class FixedCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; } = userId;
}

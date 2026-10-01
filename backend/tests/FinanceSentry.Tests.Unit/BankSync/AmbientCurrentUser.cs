namespace FinanceSentry.Tests.Unit.BankSync;

using FinanceSentry.Core.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> whose person is set per call (the handlers under test take the user id as a
/// command field), so one in-memory context can act for several people in turn. Flows with the async call
/// chain, so parallel tests do not see each other's value.
/// </summary>
public sealed class AmbientCurrentUser : ICurrentUser
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static AmbientCurrentUser Instance { get; } = new();

    public Guid? UserId => Current.Value;

    public static void ActAs(Guid? userId) => Current.Value = userId;
}

namespace FinanceSentry.Core.Auth;

/// <summary>Named ASP.NET Core authorization policies.</summary>
public static class AuthPolicies
{
    /// <summary>Satisfied only by an authenticated user holding the <see cref="AuthRoles.Owner"/> role.</summary>
    public const string RequireOwner = "RequireOwner";
}

namespace FinanceSentry.Core.Auth;

/// <summary>ASP.NET Core Identity role names. Each role is a bundle of <see cref="Permissions"/>.</summary>
public static class AuthRoles
{
    /// <summary>The account that operates this deployment; holds every permission.</summary>
    public const string Owner = "Owner";

    /// <summary>Everyone else; holds <see cref="Permissions.MemberDefaults"/> plus any per-person grants.</summary>
    public const string Member = "Member";

    public static readonly IReadOnlyList<string> All = [Owner, Member];
}

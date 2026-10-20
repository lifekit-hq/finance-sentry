namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using FinanceSentry.Core.Configuration;

/// <summary>
/// The keys of the <c>Auth</c> section the startup seeds read (<see cref="RoleSeeder"/>,
/// <see cref="SmokeAccountSeeder"/>). Both are optional: with <see cref="OwnerEmail"/> empty the owner is inferred
/// for a single-user deployment, and with <see cref="SmokeAccountOptions.Email"/> empty the smoke seed does nothing.
/// Whatever is set must be a well-formed address.
/// </summary>
public sealed class AuthSeedOptions
{
    public const string SectionName = "Auth";

    /// <summary>Email of the account that holds the Owner role (<c>Auth:OwnerEmail</c>).</summary>
    [OptionalEmailAddress]
    public string? OwnerEmail { get; set; }

    public SmokeAccountOptions SmokeAccount { get; set; } = new();
}

public sealed class SmokeAccountOptions
{
    /// <summary>Email of the passwordless account the post-deploy live smoke signs in as (<c>Auth:SmokeAccount:Email</c>).</summary>
    [OptionalEmailAddress]
    public string? Email { get; set; }
}

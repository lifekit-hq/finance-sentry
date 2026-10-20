namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Core.Configuration;

/// <summary>
/// The keys of the <c>Auth</c> section the startup seeds read (<see cref="RoleSeeder"/>,
/// <see cref="SmokeAccountSeeder"/>). Both are optional: with <see cref="OwnerEmail"/> empty the owner is inferred
/// for a single-user deployment, and with <see cref="SmokeAccountOptions.Email"/> empty the smoke seed does nothing.
/// Whatever is set must be a well-formed address.
/// </summary>
public sealed class AuthSeedOptions : IValidatableObject
{
    public const string SectionName = "Auth";

    /// <summary>Email of the account that holds the Owner role (<c>Auth:OwnerEmail</c>).</summary>
    [OptionalEmailAddress]
    public string? OwnerEmail { get; set; }

    public SmokeAccountOptions SmokeAccount { get; set; } = new();

    // DataAnnotations validation does not recurse into SmokeAccount, so its email is checked here.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!new OptionalEmailAddressAttribute().IsValid(SmokeAccount.Email))
        {
            yield return new ValidationResult(
                "The Auth:SmokeAccount:Email field is not a valid e-mail address.",
                [$"{nameof(SmokeAccount)}:{nameof(SmokeAccountOptions.Email)}"]);
        }
    }
}

public sealed class SmokeAccountOptions
{
    /// <summary>Email of the passwordless account the post-deploy live smoke signs in as (<c>Auth:SmokeAccount:Email</c>).</summary>
    public string? Email { get; set; }
}

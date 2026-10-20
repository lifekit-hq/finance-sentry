namespace FinanceSentry.Core.Configuration;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// <c>[Url]</c> that lets an unset <em>or empty</em> value through. A compose file that forwards an unset
/// variable (<c>${VAR:-}</c>) binds "" for an optional setting; stock <c>[Url]</c> rejects that.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalUrlAttribute : ValidationAttribute
{
    private static readonly UrlAttribute Url = new();

    public override bool IsValid(object? value) => value is null or "" || Url.IsValid(value);
}

/// <summary><c>[EmailAddress]</c> that lets an unset or empty value through (see <see cref="OptionalUrlAttribute"/>).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Email = new();

    public override bool IsValid(object? value) => value is null or "" || Email.IsValid(value);
}

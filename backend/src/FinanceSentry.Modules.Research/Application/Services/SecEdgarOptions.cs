namespace FinanceSentry.Modules.Research.Application.Services;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// SEC EDGAR client identity. SEC fair-access policy requires a User-Agent that names the requester
/// with a contact address, and its edge (Akamai) answers HTTP 403 to any User-Agent without an email
/// address in it: a URL contact is refused too (#1443). Operators set <c>SecEdgar:Contact</c>
/// (env <c>SEC_EDGAR_CONTACT</c>) to an email address. There is no committed default: an address
/// belongs to the operator's own configuration, never to the repo.
/// </summary>
public sealed partial class SecEdgarOptions
{
    public const string SectionName = "SecEdgar";

    public string? Contact { get; set; }

    /// <summary>Whether <paramref name="contact"/> carries an email address, the one contact form the SEC edge accepts.</summary>
    public static bool IsEmailContact(string? contact)
        => !string.IsNullOrWhiteSpace(contact) && EmailPattern().IsMatch(contact);

    /// <summary>The User-Agent every request on the <c>sec-edgar</c> client declares.</summary>
    public static string UserAgentFor(string? contact)
        => string.IsNullOrWhiteSpace(contact)
            ? "FinanceSentry/1.0"
            : $"FinanceSentry/1.0 (contact: {contact.Trim()})";

    [GeneratedRegex(@"[^\s@<>()]+@[^\s@<>()]+\.[^\s@<>()]+")]
    private static partial Regex EmailPattern();
}

/// <summary>
/// Startup validation for <see cref="SecEdgarOptions"/>. In Production a missing or non-email
/// <c>SecEdgar:Contact</c> stops the host coming up (paired with <c>ValidateOnStart()</c>) with a message
/// naming the setting, instead of the API serving with every EDGAR request refused with 403 and
/// every US-GAAP ticker blind. Elsewhere (development, tests) it logs a warning and lets the host start.
/// </summary>
public sealed class SecEdgarOptionsValidator(
    IHostEnvironment? environment = null,
    ILogger<SecEdgarOptionsValidator>? logger = null) : IValidateOptions<SecEdgarOptions>
{
    internal const string ContactNotConfigured =
        "SecEdgar:Contact is missing or is not an email address. The SEC refuses (HTTP 403) every request "
        + "whose User-Agent does not carry one, so no ticker->CIK map, filing or fundamentals can load. "
        + "Set SEC_EDGAR_CONTACT (SecEdgar__Contact) to an address the SEC may reach the operator on.";

    public ValidateOptionsResult Validate(string? name, SecEdgarOptions options)
    {
        if (SecEdgarOptions.IsEmailContact(options.Contact))
        {
            return ValidateOptionsResult.Success;
        }

        if (environment?.IsProduction() == true)
        {
            return ValidateOptionsResult.Fail(ContactNotConfigured);
        }

        logger?.LogWarning("{Message} Continuing because this is not a Production host.", ContactNotConfigured);
        return ValidateOptionsResult.Success;
    }
}

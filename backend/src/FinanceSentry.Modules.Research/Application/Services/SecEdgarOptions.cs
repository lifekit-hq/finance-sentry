namespace FinanceSentry.Modules.Research.Application.Services;

using System.Text.RegularExpressions;

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

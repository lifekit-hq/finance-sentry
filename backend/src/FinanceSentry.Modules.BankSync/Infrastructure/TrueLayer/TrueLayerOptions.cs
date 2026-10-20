namespace FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

/// <summary>
/// The <c>TrueLayer</c> configuration section (plus the root <c>PublicApiBaseUrl</c> the OAuth redirect
/// URI is built from). The integration is optional: with no <see cref="ClientId"/> and
/// <see cref="ClientSecret"/> the host still starts and the client reports "not configured" on first use.
/// Whatever is set must be well-formed, and the two credentials come together.
/// </summary>
public sealed class TrueLayerOptions
{
    public const string SectionName = "TrueLayer";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    [Url]
    public string AuthBaseUrl { get; set; } = "https://auth.truelayer-sandbox.com";

    [Url]
    public string ApiBaseUrl { get; set; } = "https://api.truelayer-sandbox.com";

    [Required]
    public string Scopes { get; set; } = "info accounts balance transactions cards offline_access";

    [RegularExpression("^/.*", ErrorMessage = "TrueLayer:CallbackPath must start with '/'.")]
    public string CallbackPath { get; set; } = "/api/v1/accounts/truelayer/callback";

    [Url]
    public string FrontendRedirectBase { get; set; } = "http://localhost:4200";

    /// <summary>Root key <c>PublicApiBaseUrl</c>, copied in at registration; not part of the section.</summary>
    [Url]
    public string PublicApiBaseUrl { get; set; } = "http://localhost:5001";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>The OAuth redirect URI TrueLayer sends the browser back to.</summary>
    public string RedirectUri => $"{PublicApiBaseUrl.TrimEnd('/')}{CallbackPath}";
}

/// <summary>A client id without a secret (or the reverse) can never authenticate; refuse it at startup.</summary>
public sealed class TrueLayerOptionsValidator : IValidateOptions<TrueLayerOptions>
{
    public ValidateOptionsResult Validate(string? name, TrueLayerOptions options) =>
        string.IsNullOrWhiteSpace(options.ClientId) != string.IsNullOrWhiteSpace(options.ClientSecret)
            ? ValidateOptionsResult.Fail("TrueLayer:ClientId and TrueLayer:ClientSecret must be set together.")
            : ValidateOptionsResult.Success;
}

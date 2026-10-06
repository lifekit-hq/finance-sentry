namespace FinanceSentry.Modules.Companion.Application.Services;

/// <summary>
/// VAPID configuration for Web Push (spec 859), bound from the <c>WebPush</c> section. Keys come from configuration
/// or secrets only and are never logged; an empty key pair means push is unavailable and startup is unaffected
/// (FR-006).
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>VAPID public key, base64url. Served to the browser as the application server key.</summary>
    public string? PublicKey { get; set; }

    /// <summary>VAPID private key, base64url. Never serialised or logged.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>VAPID contact (<c>mailto:</c> or <c>https:</c> URL) sent to push services.</summary>
    public string? Subject { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

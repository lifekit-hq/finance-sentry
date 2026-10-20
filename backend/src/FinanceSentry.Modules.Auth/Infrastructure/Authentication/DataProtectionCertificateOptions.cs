namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// The <c>DataProtection</c> configuration section: the certificate that wraps the key ring at rest. Optional
/// (local runs and tests keep the ring unprotected); when set it must load, so a bad value stops startup instead
/// of surfacing at the first protect call.
/// </summary>
public sealed class DataProtectionCertificateOptions : IValidatableObject
{
    public const string SectionName = "DataProtection";

    /// <summary>Base64 of a PKCS#12 with no password whose RSA key wraps the key ring.</summary>
    public string? CertificatePfxBase64 { get; set; }

    /// <summary>Loads the certificate, or null when none is configured. Ephemeral: the container filesystem is read-only.</summary>
    public X509Certificate2? TryLoad() =>
        string.IsNullOrWhiteSpace(CertificatePfxBase64)
            ? null
            : X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(CertificatePfxBase64), password: null, X509KeyStorageFlags.EphemeralKeySet);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        string? error = null;
        try
        {
            TryLoad()?.Dispose();
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            error = $"DataProtection:CertificatePfxBase64 must be the base64 of a PKCS#12 certificate with no password ({ex.GetType().Name}).";
        }

        if (error is not null)
            yield return new ValidationResult(error, [nameof(CertificatePfxBase64)]);
    }
}

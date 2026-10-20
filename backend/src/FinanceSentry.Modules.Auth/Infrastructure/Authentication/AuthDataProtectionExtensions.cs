namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The ASP.NET Core Data Protection key ring, persisted in the Auth database. It protects the OpenID
/// Connect state and correlation cookies. The api container is read-only with no key volume, so the platform
/// default is an in-memory ring that every restart throws away: each deploy voided any sign-in in flight
/// (issue #930).
/// </summary>
public static class AuthDataProtectionExtensions
{
    public const string ApplicationName = "FinanceSentry";

    /// <summary>Configuration key: base64 of a PKCS#12 (no password) whose RSA key wraps the key ring at rest.</summary>
    public const string CertificateConfigKey = $"{DataProtectionCertificateOptions.SectionName}:{nameof(DataProtectionCertificateOptions.CertificatePfxBase64)}";

    public static IServiceCollection AddAuthDataProtection(this IServiceCollection services, IConfiguration config)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AuthDbContext>();

        // Production sets the certificate (docker-compose.prod.yml requires it), so the keys sit encrypted in the
        // database. Without one the platform stores them unprotected and says so in the log; local runs and tests.
        // Optional: when set it must be the base64 of a loadable PKCS#12, or loading it here stops startup. The key
        // ring is wired here, before the container exists, so the value is read from the section directly.
        var certificate = config.GetSection(DataProtectionCertificateOptions.SectionName).Get<DataProtectionCertificateOptions>();
        if (certificate?.TryLoad() is { } cert)
        {
            dataProtection.ProtectKeysWithCertificate(cert);
        }

        return services;
    }
}

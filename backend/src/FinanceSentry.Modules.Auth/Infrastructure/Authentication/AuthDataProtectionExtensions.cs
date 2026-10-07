namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using System.Security.Cryptography.X509Certificates;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The ASP.NET Core Data Protection key ring, persisted in the Auth database. It protects the invite tokens
/// (Identity's token providers) and the OpenID Connect state and correlation cookies. The api container is
/// read-only with no key volume, so the platform default is an in-memory ring that every restart throws away:
/// each deploy voided outstanding invite links and any sign-in in flight (issue #930).
/// </summary>
public static class AuthDataProtectionExtensions
{
    public const string ApplicationName = "FinanceSentry";

    /// <summary>Configuration key: base64 of a PKCS#12 (no password) whose RSA key wraps the key ring at rest.</summary>
    public const string CertificateConfigKey = "DataProtection:CertificatePfxBase64";

    public static IServiceCollection AddAuthDataProtection(this IServiceCollection services, IConfiguration config)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AuthDbContext>();

        // Production sets the certificate (docker-compose.prod.yml requires it), so the keys sit encrypted in the
        // database. Without one the platform stores them unprotected and says so in the log; local runs and tests.
        var pfx = config[CertificateConfigKey];
        if (!string.IsNullOrWhiteSpace(pfx))
        {
            // Ephemeral: the container filesystem is read-only, so the key must not be written to a user store.
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfx), password: null, X509KeyStorageFlags.EphemeralKeySet));
        }

        return services;
    }
}

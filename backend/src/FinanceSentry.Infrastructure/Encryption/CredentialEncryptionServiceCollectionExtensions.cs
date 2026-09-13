namespace FinanceSentry.Infrastructure.Encryption;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class CredentialEncryptionServiceCollectionExtensions
{
    /// <summary>
    /// The credential-bearing role: the key material, its startup validation, the encryption service
    /// and startup key rotation. Registered by the worker host only (<c>IWorkerRegistrar</c>) —
    /// a host that never decrypts a credential must not hold the key, and two hosts rotating the
    /// same rows at startup is a race nobody designed (issue #613). Rotation targets are registered
    /// by the module that owns each store, as <see cref="ICredentialRotationTarget"/>.
    /// </summary>
    public static IServiceCollection AddCredentialEncryption(
        this IServiceCollection services, IConfiguration config)
    {
        // #493: validated AT STARTUP, not at first credential read. Outside Development an
        // unconfigured or disclosed key stops the process coming up; the service itself no longer
        // falls back to the key committed in this repository.
        services.AddOptions<EncryptionOptions>()
            .Bind(config.GetSection(EncryptionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
        services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();

        // #493: rows written under the old key are re-encrypted on startup.
        services.AddScoped<CredentialKeyRotationService>();
        services.AddHostedService<CredentialKeyRotationHostedService>();

        return services;
    }
}

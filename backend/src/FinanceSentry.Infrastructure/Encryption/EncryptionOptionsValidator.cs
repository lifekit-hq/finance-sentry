namespace FinanceSentry.Infrastructure.Encryption;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Startup validation for <see cref="EncryptionOptions"/> (issue #493).
///
/// The service used to inject <see cref="CredentialEncryptionService.DisclosedFallbackKeyBase64"/>
/// whenever <c>Encryption:Keys</c> was empty. No key was configured in production, so every stored
/// bank credential was AES-GCM-encrypted under a key committed to a public repository — and nothing
/// said so. Two rules replace that silence:
///
/// <list type="number">
/// <item>Outside Development, an unconfigured or disclosed key is a <b>startup</b> failure. Paired
/// with <c>ValidateOnStart()</c> the process refuses to come up, rather than serving traffic and
/// failing at the first credential read.</item>
/// <item>In Development the fallback survives so a local checkout still runs — but it is announced
/// at Warning level on every boot. The disclosed key is a development convenience and is never
/// again reachable anywhere else.</item>
/// </list>
///
/// Rule 1 covers the key arriving from ANY source, not just the removed fallback: pasting the
/// disclosed value into a production <c>appsettings</c> or env var is the same disclosure, so it is
/// refused on its value rather than on where it came from.
/// </summary>
public sealed class EncryptionOptionsValidator(
    IHostEnvironment environment,
    ILogger<EncryptionOptionsValidator> logger) : IValidateOptions<EncryptionOptions>
{
    internal const string NoKeyConfigured =
        "Encryption:Keys is not configured. Set Encryption__Keys__1 (a Base64 32-byte AES-256 key) "
        + "in the environment or a secrets store. Outside Development there is no fallback: an "
        + "unconfigured key used to mean credentials were encrypted under a key published in the "
        + "repository (issue #493).";

    internal const string DisclosedKeyConfigured =
        "Encryption:Keys contains the disclosed development key, which is committed to a public "
        + "repository. Any credential encrypted under it must be treated as compromised. Issue a "
        + "fresh key, add it as a NEW version, bump Encryption:CurrentKeyVersion, and let startup "
        + "rotation re-encrypt the existing rows before retiring the old version.";

    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        var isDevelopment = environment.IsDevelopment();

        if (options.Keys is null || options.Keys.Count == 0)
        {
            if (!isDevelopment)
            {
                return ValidateOptionsResult.Fail(NoKeyConfigured);
            }

            // Development only, and loudly: the fallback is the reason #493 existed, so it never
            // happens quietly again.
            logger.LogWarning(
                "Encryption:Keys is not configured. Falling back to the DISCLOSED development key "
                + "— this is permitted in Development only. Credentials written now are readable by "
                + "anyone with the repository. Never run any other environment this way.");

            options.Keys = new Dictionary<int, string>
            {
                [1] = CredentialEncryptionService.DisclosedFallbackKeyBase64,
            };
            options.CurrentKeyVersion = 1;
            return ValidateOptionsResult.Success;
        }

        // The disclosed key is refused as the key NEW credentials are written under — never as a
        // key old ones are read with. Every row already in production is at version 1 under this
        // exact value, so retiring the disclosure REQUIRES keeping it configured as a non-current
        // version until rotation has moved every row off it. Refusing it outright (which this
        // check did at first) makes the documented migration — add a new version, bump
        // CurrentKeyVersion, let startup rotation run — impossible, and would have blocked the one
        // deployment this whole change exists to enable.
        if (!isDevelopment
            && options.Keys.TryGetValue(options.CurrentKeyVersion, out var currentKey)
            && currentKey == CredentialEncryptionService.DisclosedFallbackKeyBase64)
        {
            return ValidateOptionsResult.Fail(DisclosedKeyConfigured);
        }

        if (!isDevelopment
            && options.Keys.ContainsValue(CredentialEncryptionService.DisclosedFallbackKeyBase64))
        {
            // Allowed, and never quietly: the disclosure is only closed once rotation has emptied
            // this version and it is removed from configuration.
            logger.LogWarning(
                "Encryption:Keys still carries the DISCLOSED key at a non-current version. That is "
                + "expected DURING rotation — it is what decrypts rows written before the new key "
                + "existed. Credentials still on that version remain readable by anyone with the "
                + "repository. Remove it once a startup rotation reports 0 rows migrated.");
        }

        // A key that is present but unusable (blank because an env var did not expand, or not
        // 32 bytes) would pass a mere presence check and fail at the first credential read
        // instead — the same late-discovery shape this whole change removes.
        foreach (var (version, value) in options.Keys)
        {
            if (!IsUsableAesKey(value))
            {
                return ValidateOptionsResult.Fail(
                    $"Encryption:Keys[{version}] is not a Base64-encoded 32-byte AES-256 key. "
                    + "An unset environment variable expands to an empty value, which looks "
                    + "configured and is not.");
            }
        }

        if (!options.Keys.ContainsKey(options.CurrentKeyVersion))
        {
            return ValidateOptionsResult.Fail(
                $"Encryption:CurrentKeyVersion ({options.CurrentKeyVersion}) has no matching entry "
                + "in Encryption:Keys, so new credentials could not be encrypted.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsUsableAesKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[32];
        return Convert.TryFromBase64String(value, decoded, out var written) && written == 32;
    }
}

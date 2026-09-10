namespace FinanceSentry.Infrastructure.Encryption;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs every <see cref="ICredentialRotationTarget"/> up to the current key version (issue #493).
///
/// On demand: resolve and call <see cref="RotateAllAsync"/>. On startup: see
/// <see cref="CredentialKeyRotationHostedService"/>.
/// </summary>
public sealed class CredentialKeyRotationService(
    IEnumerable<ICredentialRotationTarget> targets,
    IOptions<EncryptionOptions> options,
    ILogger<CredentialKeyRotationService> logger)
{
    /// <summary>
    /// Rotate every registered store, returning rows migrated per store name.
    ///
    /// Targets run in sequence, not in parallel: they share a key version and a database, and a
    /// serial run keeps the log readable and the failure attributable. One target's failure stops
    /// the run — a half-rotated store is recoverable (rotation is idempotent, so the next run
    /// resumes), whereas continuing past an unexplained decrypt failure is not.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> RotateAllAsync(
        CancellationToken cancellationToken = default)
    {
        var targetVersion = options.Value.CurrentKeyVersion;
        var migrated = new Dictionary<string, int>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = await target.RotateAsync(targetVersion, cancellationToken);
            migrated[target.Name] = count;

            if (count > 0)
            {
                logger.LogInformation(
                    "Credential key rotation: migrated {Count} row(s) in {Store} to key version {Version}.",
                    count, target.Name, targetVersion);
            }
        }

        var total = migrated.Values.Sum();
        if (total == 0)
        {
            logger.LogInformation(
                "Credential key rotation: every store is already at key version {Version}.",
                targetVersion);
        }
        else
        {
            logger.LogWarning(
                "Credential key rotation: migrated {Total} credential row(s) to key version "
                + "{Version}. The previous key version can be retired once every store reports 0.",
                total, targetVersion);
        }

        return migrated;
    }
}

/// <summary>
/// Runs credential key rotation once at startup, so deploying a new key is enough to migrate the
/// rows — no operator step, and no window in which the disclosed key is still in use because
/// somebody had to remember to trigger a job (issue #493).
///
/// Deliberately non-fatal: a rotation failure is logged and the application still starts. Refusing
/// to boot would take the whole app down over rows that are no less readable than they were a
/// second earlier, and rotation is idempotent so the next start retries. Startup validation
/// (<see cref="EncryptionOptionsValidator"/>) is the part that fails closed.
/// </summary>
public sealed class CredentialKeyRotationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CredentialKeyRotationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var rotation = scope.ServiceProvider.GetRequiredService<CredentialKeyRotationService>();
            await rotation.RotateAllAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Credential key rotation failed at startup. Rows still on an older key version "
                + "remain encrypted under it; rotation is idempotent and will retry on the next "
                + "start. Do NOT retire the previous key version until a run reports 0 rows.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

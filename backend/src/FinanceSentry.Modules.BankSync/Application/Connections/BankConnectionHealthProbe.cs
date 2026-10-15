namespace FinanceSentry.Modules.BankSync.Application.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using Microsoft.Extensions.Logging;

/// <summary>
/// The bank connections' probes (<see cref="IConnectionHealthProbe"/>), for a suspect credential failure only:
/// <list type="bullet">
/// <item>Monobank: the token's client-info, its one cheap authenticated call. Skipped while a client-info answer is
/// still cached (<see cref="MonobankBalanceCache"/>), since Monobank answers one call a minute per token; a success
/// fills that cache, so the next sync or discovery reuses it.</item>
/// <item>TrueLayer: the token refresh (<see cref="ITrueLayerTokenRefreshService.AcquireAccessTokenAsync"/>), no data
/// read. It rotates the refresh token, so it is never made for a connection that is not suspect.</item>
/// </list>
/// A Dormant connection gets no probe here: the account-discovery pass already makes these same calls at the Dormant
/// cadence (<see cref="IBankConnectionHealth.DormantPausedUntil"/>), and a Dormant account keeps its slowed sync.
/// </summary>
public sealed class BankConnectionHealthProbe(
    IMonobankCredentialRepository monobankCredentials,
    ITrueLayerConnectionRepository trueLayerConnections,
    IMonobankAdapter monobankAdapter,
    MonobankBalanceCache monobankBalanceCache,
    ITrueLayerTokenRefreshService trueLayerTokenRefresh,
    ICredentialEncryptionService encryption,
    IBankConnectionHealth connectionHealth,
    ILogger<BankConnectionHealthProbe> logger) : IConnectionHealthProbe
{
    private const string MonobankBankName = "Monobank";

    public async Task<IReadOnlyList<ConnectionHealthProbeTarget>> ListCandidatesAsync(CancellationToken ct = default)
    {
        var monobank = (await monobankCredentials.GetAllUnscopedAsync(ct))
            .Where(c => c.Health.SuspectSince is not null)
            .Select(c => Target(MonobankFailureClassifier.ProviderName, nameof(MonobankCredential), c.Id, c.UserId, c.Health));
        var trueLayer = (await trueLayerConnections.GetAllLinkedUnscopedAsync(ct))
            .Where(c => c.Health.SuspectSince is not null)
            .Select(c => Target(TrueLayerFailureClassifier.ProviderName, nameof(TrueLayerConnection), c.Id, c.UserId, c.Health));

        return [.. monobank, .. trueLayer];
    }

    public Task ProbeAsync(ConnectionHealthProbeTarget target, ConnectionHealthProbeReason reason, CancellationToken ct = default) =>
        target.Subject.Provider switch
        {
            MonobankFailureClassifier.ProviderName => ProbeMonobankAsync(target.Subject.Id, ct),
            TrueLayerFailureClassifier.ProviderName => ProbeTrueLayerAsync(target.Subject.Id, ct),
            _ => Task.CompletedTask,
        };

    private async Task ProbeMonobankAsync(Guid credentialId, CancellationToken ct)
    {
        if (await monobankCredentials.GetByIdUnscopedAsync(credentialId, ct) is not { } credential)
            return;

        var connection = new BankConnectionRef(
            MonobankFailureClassifier.ProviderName, credential.Id, credential.UserId, MonobankBankName);
        try
        {
            var token = CredentialUnreadableException.Decrypt(
                encryption, credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion);
            if (monobankBalanceCache.HasFreshEntries(token))
            {
                logger.LogInformation(
                    "Connection health probe: Monobank client-info fetched recently for credential {CredentialId}; probing next window",
                    credential.Id);
                return;
            }

            var clientInfo = await monobankAdapter.GetClientInfoAsync(token, ct);
            foreach (var account in clientInfo.Accounts)
                monobankBalanceCache.Set(token, account.Id, MonobankAdapter.ToBankAccountInfo(account, clientInfo.Name));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogFailure(connection, ex);
            await connectionHealth.RecordConnectionProbeFailureAsync(connection, ex, CancellationToken.None);
            return;
        }

        await connectionHealth.RecordConnectionSuccessAsync(connection, CancellationToken.None);
    }

    private async Task ProbeTrueLayerAsync(Guid connectionId, CancellationToken ct)
    {
        if (await trueLayerConnections.GetByIdUnscopedAsync(connectionId, ct) is not { } stored)
            return;

        var connection = new BankConnectionRef(
            TrueLayerFailureClassifier.ProviderName, stored.Id, stored.UserId, stored.ProviderDisplayName);
        try
        {
            await trueLayerTokenRefresh.AcquireAccessTokenAsync(stored.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogFailure(connection, ex);
            await connectionHealth.RecordConnectionProbeFailureAsync(connection, ex, CancellationToken.None);
            return;
        }

        await connectionHealth.RecordConnectionSuccessAsync(connection, CancellationToken.None);
    }

    private void LogFailure(BankConnectionRef connection, Exception ex) =>
        logger.LogWarning(
            ex,
            "Connection health probe: {Provider} connection {ConnectionId} of user {UserId} failed its probe",
            connection.Provider,
            connection.Id,
            connection.UserId);

    private static ConnectionHealthProbeTarget Target(string provider, string kind, Guid id, Guid userId, ConnectionHealth health) =>
        new(new ConnectionHealthSubject(provider, kind, id, userId), health, ProbesDormant: false);
}

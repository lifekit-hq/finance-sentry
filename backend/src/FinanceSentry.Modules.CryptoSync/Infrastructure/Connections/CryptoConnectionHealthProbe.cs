namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// The crypto venues' probes (<see cref="IConnectionHealthProbe"/>): the venue adapter's credential check, which is
/// Binance's signed account endpoint (<c>GET /api/v3/account</c>) and, Revolut X having no account endpoint, its smallest
/// signed read (<c>GET /balances</c>) - no holdings, prices or trades are fetched or written.
/// </summary>
public sealed class CryptoConnectionHealthProbe(
    IExchangeCredentialRepository credentials,
    CryptoExchangeAdapterRegistry adapters,
    ICredentialEncryptionService encryption,
    BinanceFailureClassifier binanceClassifier,
    RevolutXFailureClassifier revolutXClassifier,
    ConnectionHealthProbeRecorder recorder) : IConnectionHealthProbe
{
    private static readonly string[] Providers = [CryptoExchangeProvider.Binance, CryptoExchangeProvider.RevolutX];

    public async Task<IReadOnlyList<ConnectionHealthProbeTarget>> ListCandidatesAsync(CancellationToken ct = default)
    {
        var targets = new List<ConnectionHealthProbeTarget>();
        foreach (var provider in Providers)
        {
            targets.AddRange((await credentials.GetAllActiveUnscopedAsync(provider, ct))
                .Where(c => c.Health.SuspectSince is not null || c.Health.State == ConnectionHealthState.Dormant)
                .Select(c => new ConnectionHealthProbeTarget(
                    new ConnectionHealthSubject(provider, nameof(ExchangeCredential), c.Id, c.UserId), c.Health)));
        }

        return targets;
    }

    public async Task ProbeAsync(ConnectionHealthProbeTarget target, ConnectionHealthProbeReason reason, CancellationToken ct = default)
    {
        var subject = target.Subject;
        if (await credentials.GetUnscopedAsync(subject.UserId, subject.Provider, ct) is not { IsActive: true } credential
            || credential.Id != subject.Id)
            return;

        IProviderFailureClassifier classifier = subject.Provider == CryptoExchangeProvider.Binance
            ? binanceClassifier
            : revolutXClassifier;

        await recorder.ProbeAsync(
            subject,
            credential.Health,
            async c =>
            {
                var (apiKey, apiSecret) = Decrypt(credential);
                await adapters.Get(subject.Provider).ValidateCredentialsAsync(apiKey, apiSecret, c);
            },
            classifier,
            (health, c) =>
            {
                credential.ApplyHealth(health);
                return credentials.SaveHealthUnscopedAsync(credential.Id, health, c);
            },
            ct);
    }

    private (string ApiKey, string ApiSecret) Decrypt(ExchangeCredential credential)
    {
        try
        {
            return (
                encryption.Decrypt(credential.EncryptedApiKey, credential.ApiKeyIv, credential.ApiKeyAuthTag, credential.KeyVersion),
                encryption.Decrypt(
                    credential.EncryptedApiSecret, credential.ApiSecretIv, credential.ApiSecretAuthTag, credential.KeyVersion));
        }
        catch (Exception ex)
        {
            throw new ExchangeCredentialUnreadableException(credential.Provider, ex);
        }
    }
}

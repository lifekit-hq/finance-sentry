namespace FinanceSentry.Modules.BankSync.Infrastructure.Maintenance;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using Microsoft.Extensions.Logging;

/// <summary>
/// Re-reads an account's recent transactions from its provider, for maintenance passes that need to know what
/// the bank reports now (the sync itself only reads from its watermark). Monobank is walked in 31-day windows
/// (its statement cap; a 429 is retried by the Monobank client), TrueLayer returns its own ~90-day window
/// through the shared token refresh.
/// </summary>
public interface IProviderHistoryFetcher
{
    /// <summary>The account's transactions from <paramref name="since"/> to now; empty when the provider cannot be read.</summary>
    Task<IReadOnlyList<TransactionCandidate>> FetchAsync(BankAccount account, DateTime since, CancellationToken ct = default);
}

/// <inheritdoc />
public class ProviderHistoryFetcher(
    ICredentialEncryptionService encryption,
    IBankProviderFactory providerFactory,
    IMonobankCredentialRepository monobankCredentials,
    IMonobankAdapter monobankAdapter,
    ITrueLayerConnectionRepository truelayerConnections,
    ITrueLayerTokenRefreshService truelayerTokenRefresh,
    ILogger<ProviderHistoryFetcher> logger) : IProviderHistoryFetcher
{
    private const int MonobankWindowDays = 31;
    private const string LinkedStatus = "LINKED";

    private readonly ICredentialEncryptionService _encryption = encryption;
    private readonly IBankProviderFactory _providerFactory = providerFactory;
    private readonly IMonobankCredentialRepository _monobankCredentials = monobankCredentials;
    private readonly IMonobankAdapter _monobankAdapter = monobankAdapter;
    private readonly ITrueLayerConnectionRepository _truelayerConnections = truelayerConnections;
    private readonly ITrueLayerTokenRefreshService _truelayerTokenRefresh = truelayerTokenRefresh;
    private readonly ILogger<ProviderHistoryFetcher> _logger = logger;

    public Task<IReadOnlyList<TransactionCandidate>> FetchAsync(BankAccount account, DateTime since, CancellationToken ct = default) =>
        account.Provider switch
        {
            "monobank" => FetchMonobankAsync(account, since, ct),
            "truelayer" => FetchTrueLayerAsync(account, ct),
            _ => throw new InvalidOperationException($"Unknown provider '{account.Provider}' for account {account.Id}."),
        };

    private async Task<IReadOnlyList<TransactionCandidate>> FetchMonobankAsync(BankAccount account, DateTime since, CancellationToken ct)
    {
        if (account.MonobankCredentialId is null)
            throw new InvalidOperationException($"Monobank account {account.Id} has no credential id.");

        var cred = await _monobankCredentials.GetByIdUnscopedAsync(account.MonobankCredentialId.Value, ct)
            ?? throw new InvalidOperationException($"Monobank credential {account.MonobankCredentialId} not found.");
        var token = _encryption.Decrypt(cred.EncryptedToken, cred.Iv, cred.AuthTag, cred.KeyVersion);

        var windowStart = new DateTimeOffset(DateTime.SpecifyKind(since.AddDays(-1), DateTimeKind.Utc));
        var now = DateTimeOffset.UtcNow;

        var all = new List<TransactionCandidate>();
        while (windowStart < now)
        {
            var windowEnd = windowStart.AddDays(MonobankWindowDays);
            if (windowEnd > now)
                windowEnd = now;

            var candidates = await _monobankAdapter.GetCandidatesAsync(
                token, account.ExternalAccountId, account.Id, account.UserId, windowStart, windowEnd, ct);
            all.AddRange(candidates);

            _logger.LogInformation(
                "Monobank window {From:yyyy-MM-dd}..{To:yyyy-MM-dd} for account {AccountId}: {Count} transactions.",
                windowStart, windowEnd, account.Id, candidates.Count);

            windowStart = windowEnd;
        }
        return all;
    }

    private async Task<IReadOnlyList<TransactionCandidate>> FetchTrueLayerAsync(BankAccount account, CancellationToken ct)
    {
        if (account.TrueLayerConnectionId is null)
            throw new InvalidOperationException($"TrueLayer account {account.Id} has no connection id.");

        var connection = await _truelayerConnections.GetByIdUnscopedAsync(account.TrueLayerConnectionId.Value, ct)
            ?? throw new InvalidOperationException($"TrueLayer connection {account.TrueLayerConnectionId} not found.");

        // Only a linked consent can be refreshed; an expired one waits for the user to reconnect.
        if (connection.Status != LinkedStatus)
        {
            _logger.LogInformation(
                "Provider history skipped for account {AccountId}: TrueLayer connection {ConnectionId} is {Status}.",
                account.Id, connection.Id, connection.Status);
            return [];
        }

        var accessToken = await _truelayerTokenRefresh.AcquireAccessTokenAsync(connection.Id, ct);

        var (candidates, _) = await _providerFactory.Resolve("truelayer")
            .SyncTransactionsAsync(accessToken, account.ExternalAccountId, account.Id, account.UserId, null, ct);
        return candidates;
    }
}

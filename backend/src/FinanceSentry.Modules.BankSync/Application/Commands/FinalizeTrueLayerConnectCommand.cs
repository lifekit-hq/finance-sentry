namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public record FinalizeTrueLayerConnectCommand(string Reference, string Code) : ICommand<FinalizeTrueLayerConnectResult>;

public record FinalizeTrueLayerConnectResult(
    Guid UserId,
    int AccountsConnected,
    IReadOnlyList<Guid> CreatedAccountIds);

public class FinalizeTrueLayerConnectCommandHandler(
    ITrueLayerClient client,
    ITrueLayerConnectionRepository connections,
    ICredentialEncryptionService encryption,
    IBankAccountRepository accounts,
    IScheduledSyncService syncService,
    IConfiguration configuration,
    ILogger<FinalizeTrueLayerConnectCommandHandler> logger)
    : ICommandHandler<FinalizeTrueLayerConnectCommand, FinalizeTrueLayerConnectResult>
{
    public async Task<FinalizeTrueLayerConnectResult> Handle(
        FinalizeTrueLayerConnectCommand request, CancellationToken cancellationToken)
    {
        var connection = await connections.GetByReferenceAsync(request.Reference, cancellationToken)
            ?? throw new TrueLayerException(
                "TRUELAYER_CONNECTION_NOT_FOUND",
                $"No TrueLayer connection found for state '{request.Reference}'.",
                404);

        if (connection.Status == "LINKED")
            return new FinalizeTrueLayerConnectResult(connection.UserId, connection.BankAccounts.Count, []);

        var callbackPath = configuration["TrueLayer:CallbackPath"]
            ?? "/api/v1/accounts/truelayer/callback";
        var publicApiBase = configuration["PublicApiBaseUrl"]
            ?? "http://localhost:5001";
        var redirectUri = $"{publicApiBase.TrimEnd('/')}{callbackPath}";

        var tokens = await client.ExchangeCodeAsync(request.Code, redirectUri, cancellationToken);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
            throw new TrueLayerException(
                "TRUELAYER_NO_REFRESH_TOKEN",
                "Bank returned no refresh token. Ensure offline_access scope is requested.",
                400);

        var encrypted = encryption.Encrypt(tokens.RefreshToken);
        connection.SetRefreshToken(encrypted.Ciphertext, encrypted.Iv, encrypted.AuthTag, encrypted.KeyVersion);
        connection.MarkLinked(expiresAt: DateTime.UtcNow.AddDays(90));
        await connections.UpdateAsync(connection, cancellationToken);

        var providerAccounts = await client.ListAccountsAsync(tokens.AccessToken, cancellationToken);
        var createdIds = new List<Guid>();

        foreach (var pa in providerAccounts)
        {
            decimal? currentBalance = null;
            try
            {
                var bal = await client.GetBalanceAsync(tokens.AccessToken, pa.AccountId, cancellationToken);
                currentBalance = bal?.Current;
            }
            catch (TrueLayerException)
            {
                // Best-effort: skip balance, account is still usable.
            }

            var existing = await accounts.GetByExternalAccountIdAsync(pa.AccountId, cancellationToken);
            if (existing != null)
            {
                // Reconnect/reauth: heal the existing account in place instead of skipping it.
                // Re-point it at the freshly linked connection and clear reauth_required so the
                // scheduler resumes syncing. A follow-up sync is enqueued below to backfill data.
                existing.MarkReconnected(connection.Id, currentBalance ?? existing.CurrentBalance ?? 0m);
                await accounts.UpdateAsync(existing, cancellationToken);
                createdIds.Add(existing.Id);
                continue;
            }

            var account = new BankAccount(
                userId: connection.UserId,
                externalAccountId: pa.AccountId,
                bankName: !string.IsNullOrWhiteSpace(pa.ProviderName) ? pa.ProviderName : connection.ProviderDisplayName,
                accountType: pa.AccountType,
                accountNumberLast4: pa.AccountNumberLast4,
                ownerName: string.Empty,
                currency: pa.Currency,
                createdBy: connection.UserId,
                provider: "truelayer")
            {
                TrueLayerConnectionId = connection.Id,
                CurrentBalance = currentBalance
            };

            await accounts.AddAsync(account, cancellationToken);
            createdIds.Add(account.Id);
        }

        // Credit cards live behind /data/v1/cards, not /data/v1/accounts — without this pass
        // a Revolut/AIB credit card is invisible to the whole system. Card support is
        // provider-dependent, so a failing /cards call means "no cards", not a failed connect.
        IReadOnlyList<TrueLayerAccountInfo> providerCards = [];
        try
        {
            providerCards = await client.ListCardsAsync(tokens.AccessToken, cancellationToken);
        }
        catch (TrueLayerException ex)
        {
            logger.LogInformation(
                "TrueLayer /cards unavailable for connection {ConnectionId}: {Error}",
                connection.Id, ex.Message);
        }

        foreach (var card in providerCards)
        {
            decimal? owed = null;
            decimal? creditLimit = null;
            try
            {
                var bal = await client.GetCardBalanceAsync(tokens.AccessToken, card.AccountId, cancellationToken);
                owed = bal?.Current;
                creditLimit = bal?.CreditLimit;
            }
            catch (TrueLayerException)
            {
                // Best-effort: skip balance, card is still usable.
            }

            var existing = await accounts.GetByExternalAccountIdAsync(card.AccountId, cancellationToken);
            if (existing != null)
            {
                existing.MarkReconnected(connection.Id, owed ?? existing.CurrentBalance ?? 0m);
                existing.CreditLimit = creditLimit ?? existing.CreditLimit;
                await accounts.UpdateAsync(existing, cancellationToken);
                createdIds.Add(existing.Id);
                continue;
            }

            var cardAccount = new BankAccount(
                userId: connection.UserId,
                externalAccountId: card.AccountId,
                bankName: !string.IsNullOrWhiteSpace(card.ProviderName) ? card.ProviderName : connection.ProviderDisplayName,
                accountType: "credit",
                accountNumberLast4: card.AccountNumberLast4,
                ownerName: string.Empty,
                currency: card.Currency,
                createdBy: connection.UserId,
                provider: "truelayer")
            {
                TrueLayerConnectionId = connection.Id,
                CurrentBalance = owed,
                CreditLimit = creditLimit,
                ProductType = TrueLayerAdapter.CardProductType
            };

            await accounts.AddAsync(cardAccount, cancellationToken);
            createdIds.Add(cardAccount.Id);
        }

        // Sync inline with the freshly-exchanged access token while its SCA session is still
        // active — this is the only window in which strict banks (e.g. AIB) will return
        // transaction history; a deferred background job would refresh the token and lose SCA.
        // Failures here don't fail the reconnect: the connection is already linked and the
        // recurring scheduler will retry (balance-only for SCA-locked providers).
        foreach (var accountId in createdIds)
        {
            try
            {
                await syncService.PerformFullSyncAsync(
                    accountId, ct: cancellationToken,
                    preAcquiredTrueLayerAccessToken: tokens.AccessToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Inline reconnect sync failed for account {AccountId}; scheduler will retry.",
                    accountId);
            }
        }

        return new FinalizeTrueLayerConnectResult(
            UserId: connection.UserId,
            AccountsConnected: createdIds.Count,
            CreatedAccountIds: createdIds);
    }
}

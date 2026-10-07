using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public enum InzhurSyncOutcome
{
    /// <summary>Refreshed, read and stored.</summary>
    Synced,

    /// <summary>No active connection with a session to use; nothing was called.</summary>
    Skipped,
}

public interface IInzhurSyncService
{
    /// <summary>
    /// One read of the user's cabinet: refresh the session, read holdings and cash, store them. Throws
    /// <see cref="InzhurApiException"/> when Inzhur fails; a dead session also flips the connection to
    /// <see cref="InzhurConnectionStatus.ReauthRequired"/> first. Never signs in, so never sends an SMS.
    /// </summary>
    Task<InzhurSyncOutcome> SyncAsync(Guid userId, CancellationToken ct = default);
}

public sealed class InzhurSyncService(
    IInzhurCredentialRepository credentialRepository,
    IBrokerageHoldingRepository holdingRepository,
    IInzhurApiClient api,
    ICredentialEncryptionService encryption,
    TimeProvider clock,
    ILogger<InzhurSyncService> logger) : IInzhurSyncService
{
    public async Task<InzhurSyncOutcome> SyncAsync(Guid userId, CancellationToken ct = default)
    {
        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId, ct);
        if (credential is null || credential.Status != InzhurConnectionStatus.Active || !credential.HasSession)
            return InzhurSyncOutcome.Skipped;

        try
        {
            var session = InzhurSession.Deserialize(encryption.Decrypt(
                credential.EncryptedSession, credential.SessionIv, credential.SessionAuthTag, credential.SessionKeyVersion));

            // The refresh may rotate the refresh cookie: store the new jar before anything else can fail.
            var refreshed = await api.RefreshAsync(session, ct);
            credential.RefreshSession(Encrypt(refreshed.Serialize()), Now);
            await credentialRepository.SaveChangesAsync(ct);

            var portfolio = await api.GetPortfolioAsync(refreshed, ct);
            LogUnmodelledAssetFields(portfolio);

            var positions = InzhurHoldingsMapper.Map(portfolio);
            await StoreHoldingsAsync(userId, positions, ct);

            credential.RecordSyncSuccess(Now);
            await credentialRepository.SaveChangesAsync(ct);

            logger.LogInformation(
                "Inzhur sync stored {PositionCount} positions for user {UserId}; session started {SessionStartedAt:o}",
                positions.Count, userId, credential.SessionStartedAt);
            return InzhurSyncOutcome.Synced;
        }
        catch (InzhurApiException ex) when (ex.Kind == InzhurFailureKind.ReauthRequired)
        {
            // Session lifetime is the open question the first weeks answer; log it when the chain breaks.
            logger.LogWarning(
                "Inzhur session for user {UserId} ended (started {SessionStartedAt:o}, last refreshed {SessionRefreshedAt:o}); reconnect needed",
                userId, credential.SessionStartedAt, credential.SessionRefreshedAt);
            credential.MarkReauthRequired(ex.Message);
            await credentialRepository.SaveChangesAsync(ct);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            credential.RecordSyncError(ex.Message);
            await credentialRepository.SaveChangesAsync(ct);
            throw;
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private EncryptedSecret Encrypt(string plaintext)
    {
        var result = encryption.Encrypt(plaintext);
        return new EncryptedSecret(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);
    }

    private async Task StoreHoldingsAsync(Guid userId, IReadOnlyList<InzhurPosition> positions, CancellationToken ct)
    {
        var syncedAt = Now;
        await holdingRepository.UpsertRangeAsync(positions.Select(p => new BrokerageHolding(
            userId,
            p.Symbol,
            p.InstrumentType,
            p.Quantity,
            p.UsdValue,
            InzhurHoldingsMapper.Provider,
            averageCostUsd: p.AverageCostUsd,
            acquiredAt: p.AverageCostUsd.HasValue ? syncedAt : null)), ct);
        await holdingRepository.SaveChangesAsync(ct);

        var bySymbol = positions.ToDictionary(p => p.Symbol, StringComparer.Ordinal);
        var persisted = (await holdingRepository.GetByUserIdUnscopedAsync(userId, ct))
            .Where(h => h.Provider == InzhurHoldingsMapper.Provider)
            .ToList();

        // Reconcile: a position Inzhur no longer returns (sold, redeemed bond) leaves; other providers' rows are untouched.
        var stale = persisted.Where(h => !bySymbol.ContainsKey(h.Symbol)).ToList();
        if (stale.Count > 0)
            holdingRepository.RemoveRange(stale);

        foreach (var holding in persisted)
        {
            if (bySymbol.TryGetValue(holding.Symbol, out var position))
                holding.SetCostBasis(position.AverageCostUsd, position.AverageCostUsd.HasValue ? (holding.AcquiredAt ?? syncedAt) : holding.AcquiredAt);
        }

        await holdingRepository.SaveChangesAsync(ct);
    }

    // The asset row's name field is not settled by the design read (the cabinet takes names from its CMS); the
    // field names - never values - show where it lives so the symbol can follow it.
    private void LogUnmodelledAssetFields(InzhurPortfolio portfolio)
    {
        var names = portfolio.Assets
            .SelectMany(a => a.Extra?.Keys ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (names.Count > 0)
            logger.LogInformation("Inzhur asset rows carry unmodelled fields: {FieldNames}", string.Join(", ", names));
    }
}

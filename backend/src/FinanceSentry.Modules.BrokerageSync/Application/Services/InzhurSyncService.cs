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
    /// One read of the user's cabinet: refresh the session, read holdings, cash and the whole ledger, store them (the
    /// first sync is the full history backfill; later ones re-read it and change nothing they already hold). Throws
    /// <see cref="InzhurApiException"/> when Inzhur fails; a dead session also flips the connection to
    /// <see cref="InzhurConnectionStatus.ReauthRequired"/> first. Never signs in, so never sends an SMS.
    /// </summary>
    Task<InzhurSyncOutcome> SyncAsync(Guid userId, CancellationToken ct = default);
}

public sealed class InzhurSyncService(
    IInzhurCredentialRepository credentialRepository,
    IBrokerageHoldingRepository holdingRepository,
    IBrokerageTradeRepository tradeRepository,
    IBrokerageCashTransactionRepository cashTransactionRepository,
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

            // Every read comes before any write: a failed read keeps what is stored instead of half of a new day.
            var portfolio = await api.GetPortfolioAsync(refreshed, ct);
            var transactions = await api.GetTransactionsAsync(refreshed, ct);
            LogUnmodelledAssetFields(portfolio);

            var positions = InzhurHoldingsMapper.Map(portfolio);
            RefuseWhenFundedAssetsAllDropped(portfolio, positions);
            var ledger = InzhurLedgerMapper.Map(transactions);
            if (ledger.SkippedTypes.Count > 0)
                logger.LogWarning(
                    "Inzhur ledger rows of unmodelled types were not stored: {TransactionTypes}", string.Join(", ", ledger.SkippedTypes));

            await StoreHoldingsAsync(userId, positions, ct);
            var (tradesAdded, cashAdded) = await StoreLedgerAsync(userId, ledger, ct);

            credential.RecordSyncSuccess(Now);
            await credentialRepository.SaveChangesAsync(ct);

            logger.LogInformation(
                "Inzhur sync stored {PositionCount} positions and read {LedgerRowCount} ledger rows ({TradesAdded} new trades, {CashAdded} new cash rows) for user {UserId}; session started {SessionStartedAt:o}",
                positions.Count, transactions.Count, tradesAdded, cashAdded, userId, credential.SessionStartedAt);
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

    // Assets that carry money but none of which became a position mean the cabinet moved a field, not that the owner
    // sold out: refuse the read so the stored rows are kept. Assets with no amounts (a sold-out account) still clear.
    private static void RefuseWhenFundedAssetsAllDropped(InzhurPortfolio portfolio, IReadOnlyList<InzhurPosition> positions)
    {
        var funded = portfolio.Assets
            .Where(a => a.Details is { TotalAmountUah: not (null or 0m) } or { InvestedUah: not (null or 0m) })
            .ToList();
        // Funded assets but not one holding besides cash: the quantity field moved, the owner did not sell out.
        if (funded.Count > 0 && positions.All(p => p.InstrumentType == InzhurHoldingsMapper.CashInstrumentType))
            throw new InzhurApiException(
                InzhurFailureKind.Unexpected, "Inzhur user-assets listed assets with amounts but none could be read as a position.");
    }

    private const string OpenIndicator = "O";
    private const string CloseIndicator = "C";
    private const int TradeIdMaxLength = 30;

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
            acquiredAt: p.AverageCostUsd.HasValue ? syncedAt : null,
            currency: p.Currency,
            nativeValue: p.NativeValue)), ct);
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

    // Idempotent on Inzhur's own row id: a trade seen again is refreshed in place (the cabinet may correct it), a cash
    // row seen again is left alone, as IbkrFlexTradeSyncService does for Flex rows.
    private async Task<(int TradesAdded, int CashAdded)> StoreLedgerAsync(Guid userId, InzhurLedger ledger, CancellationToken ct)
    {
        const string provider = InzhurHoldingsMapper.Provider;
        const string currency = InzhurHoldingsMapper.DefaultCurrency;
        var tradesAdded = 0;
        foreach (var t in ledger.Trades)
        {
            var openClose = t.Quantity > 0m ? OpenIndicator : CloseIndicator;
            var existing = await tradeRepository.GetByExecutionIdUnscopedAsync(userId, provider, t.Id, ct);
            if (existing is null)
            {
                await tradeRepository.AddAsync(new BrokerageTrade(
                    userId, provider, t.Id, TradeIdFor(t.Id), conid: null, t.Isin, t.Symbol, openClose, openDateTime: null,
                    t.TradeDateTime, t.Quantity, t.Price, t.Proceeds, costBasis: null, realizedPnl: null, commission: null,
                    commissionCurrency: null, taxes: null, currency, fxRateToBase: null), ct);
                tradesAdded++;
            }
            else
            {
                existing.Refresh(
                    conid: null, t.Isin, t.Symbol, openClose, openDateTime: null, t.TradeDateTime, t.Quantity, t.Price,
                    t.Proceeds, costBasis: null, realizedPnl: null, commission: null, commissionCurrency: null, taxes: null,
                    currency, fxRateToBase: null, instrumentId: null);
                tradeRepository.Update(existing);
            }
        }

        if (ledger.Trades.Count > 0)
            await tradeRepository.SaveChangesAsync(ct);

        var cashAdded = 0;
        foreach (var c in ledger.Cash)
        {
            if (await cashTransactionRepository.GetByIdempotencyKeyUnscopedAsync(userId, provider, c.Id, ct) is not null)
                continue;
            await cashTransactionRepository.AddAsync(new BrokerageCashTransaction(
                userId, provider, c.Id, conid: null, c.Isin, c.Symbol, c.DateTime, c.Amount, c.Type, c.Code,
                withholdingTax: null, currency, fxRateToBase: null), ct);
            cashAdded++;
        }

        if (cashAdded > 0)
            await cashTransactionRepository.SaveChangesAsync(ct);

        return (tradesAdded, cashAdded);
    }

    // The trade-id column is IBKR's (30 characters, informational, never a key); the full Inzhur id is the execution id.
    private static string TradeIdFor(string id) => id.Length <= TradeIdMaxLength ? id : id[..TradeIdMaxLength];

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

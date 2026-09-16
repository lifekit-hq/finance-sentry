using System.ComponentModel;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetSyncHealthTool(
    BankSyncDbContext bankSyncDbContext,
    CryptoSyncDbContext cryptoSyncDbContext,
    BrokerageSyncDbContext brokerageSyncDbContext,
    IIdentityResolver identity,
    ILogger<GetSyncHealthTool> logger)
{
    private readonly BankSyncDbContext _bankSync = bankSyncDbContext;
    private readonly CryptoSyncDbContext _cryptoSync = cryptoSyncDbContext;
    private readonly BrokerageSyncDbContext _brokerageSync = brokerageSyncDbContext;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetSyncHealthTool> _logger = logger;

    [McpServerTool(Name = "get_sync_health")]
    [Description("Returns the last sync timestamp, status, and error for each provider (Monobank, TrueLayer, Binance, Revolut X, IBKR). Defaults to the authenticated MCP identity when userId is omitted.")]
    public async Task<IReadOnlyList<SyncHealthEntry>> ExecuteAsync(
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        return
        [
            await GetMonobankHealthAsync(userIdVal, cancellationToken),
            await GetTrueLayerHealthAsync(userIdVal, cancellationToken),
            await GetExchangeHealthAsync(userIdVal, CryptoExchangeProvider.Binance, cancellationToken),
            await GetExchangeHealthAsync(userIdVal, CryptoExchangeProvider.RevolutX, cancellationToken),
            await GetIbkrHealthAsync(userIdVal, cancellationToken),
        ];
    }

    private async Task<SyncHealthEntry> GetMonobankHealthAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var credential = await _bankSync.MonobankCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(mc => mc.UserId == userId, ct);

            if (credential is null)
                return new SyncHealthEntry("monobank", null, "never_synced", null);

            // MonobankCredential has no LastSyncError; derive error state from associated accounts.
            var failedAccount = await _bankSync.BankAccounts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.Provider == "monobank" && a.SyncStatus == "failed" && a.IsActive)
                .Select(a => new { a.LastSyncError })
                .FirstOrDefaultAsync(ct);

            if (failedAccount is not null)
                return new SyncHealthEntry("monobank", credential.LastSyncAt, "error", failedAccount.LastSyncError);

            if (credential.LastSyncAt is null)
                return new SyncHealthEntry("monobank", null, "never_synced", null);

            return new SyncHealthEntry("monobank", credential.LastSyncAt, "ok", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve Monobank sync health for user {UserId}.", userId);
            return new SyncHealthEntry("monobank", null, "error", "Health check unavailable.");
        }
    }

    private async Task<SyncHealthEntry> GetTrueLayerHealthAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var accounts = await _bankSync.BankAccounts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.Provider == "truelayer" && a.IsActive)
                .Select(a => new { a.SyncStatus, a.LastSyncError, a.UpdatedAt })
                .ToListAsync(ct);

            if (accounts.Count == 0 || accounts.All(a => a.SyncStatus == "pending"))
                return new SyncHealthEntry("truelayer", null, "never_synced", null);

            var syncedAccounts = accounts.Where(a => a.SyncStatus is "active" or "failed").ToList();
            DateTime? lastSyncAt = syncedAccounts.Count > 0 ? syncedAccounts.Max(a => a.UpdatedAt) : null;

            var latestFailed = syncedAccounts
                .Where(a => a.SyncStatus == "failed")
                .OrderByDescending(a => a.UpdatedAt)
                .FirstOrDefault();

            return latestFailed is not null
                ? new SyncHealthEntry("truelayer", lastSyncAt, "error", latestFailed.LastSyncError)
                : new SyncHealthEntry("truelayer", lastSyncAt, "ok", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve TrueLayer sync health for user {UserId}.", userId);
            return new SyncHealthEntry("truelayer", null, "error", "Health check unavailable.");
        }
    }

    private async Task<SyncHealthEntry> GetExchangeHealthAsync(Guid userId, string provider, CancellationToken ct)
    {
        try
        {
            var credential = await _cryptoSync.ExchangeCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, ct);

            if (credential is null)
                return new SyncHealthEntry(provider, null, "never_synced", null);

            // MarkSyncFailed sets LastSyncError without updating LastSyncAt, so an error can exist
            // before a first successful sync.
            if (credential.LastSyncError is not null)
                return new SyncHealthEntry(provider, credential.LastSyncAt, "error", credential.LastSyncError);

            if (credential.LastSyncAt is null)
                return new SyncHealthEntry(provider, null, "never_synced", null);

            return new SyncHealthEntry(provider, credential.LastSyncAt, "ok", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve {Provider} sync health for user {UserId}.", provider, userId);
            return new SyncHealthEntry(provider, null, "error", "Health check unavailable.");
        }
    }

    private async Task<SyncHealthEntry> GetIbkrHealthAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var credential = await _brokerageSync.IBKRCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId, ct);

            if (credential is null)
                return new SyncHealthEntry("ibkr", null, "never_synced", null);

            // RecordSyncError sets LastSyncError without updating LastSyncAt, matching ExchangeCredential.
            if (credential.LastSyncError is not null)
                return new SyncHealthEntry("ibkr", credential.LastSyncAt, "error", credential.LastSyncError);

            if (credential.LastSyncAt is null)
                return new SyncHealthEntry("ibkr", null, "never_synced", null);

            return new SyncHealthEntry("ibkr", credential.LastSyncAt, "ok", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve IBKR sync health for user {UserId}.", userId);
            return new SyncHealthEntry("ibkr", null, "error", "Health check unavailable.");
        }
    }
}

public sealed record SyncHealthEntry(
    string Provider,
    DateTime? LastSyncAt,
    string Status,
    string? ErrorMessage);

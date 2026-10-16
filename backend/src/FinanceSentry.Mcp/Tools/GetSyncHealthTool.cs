using System.ComponentModel;
using System.Text.Json.Serialization;
using FinanceSentry.Core.Connections;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetSyncHealthTool(
    BankSyncDbContext bankSyncDbContext,
    CryptoSyncDbContext cryptoSyncDbContext,
    BrokerageSyncDbContext brokerageSyncDbContext,
    IIdentityResolver identity,
    ILogger<GetSyncHealthTool> logger,
    IEnumerable<IConnectionHealthReader> healthReaders,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions)
{
    private readonly BankSyncDbContext _bankSync = bankSyncDbContext;
    private readonly CryptoSyncDbContext _cryptoSync = cryptoSyncDbContext;
    private readonly BrokerageSyncDbContext _brokerageSync = brokerageSyncDbContext;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetSyncHealthTool> _logger = logger;

    private const string ReauthRequiredStatus = "reauth_required";
    private const string ExpiredConnectionStatus = "EXPIRED";
    private const string ReauthRequiredMessage = "Reconnection required.";
    private const string RetryingMessage = "The provider keeps failing; it is retried automatically and needs nothing from you.";

    [McpServerTool(Name = "get_sync_health")]
    [Description("Returns the last sync timestamp, status, and error for each provider (Monobank, TrueLayer, Binance, Revolut X, IBKR). " +
        "For a provider whose connection health is policy-driven it also returns health: healthy, retrying (failing, retried " +
        "automatically, nothing to do), reconnect_needed (only the user can fix it by reconnecting) or dormant (failing for days, " +
        "possibly closed); status is then error only for a failure the user is told about.")]
    public async Task<IReadOnlyList<SyncHealthEntry>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        IReadOnlyList<SyncHealthEntry> entries =
        [
            await GetMonobankHealthAsync(userIdVal, cancellationToken),
            await GetTrueLayerHealthAsync(userIdVal, cancellationToken),
            await GetExchangeHealthAsync(userIdVal, CryptoExchangeProvider.Binance, cancellationToken),
            await GetExchangeHealthAsync(userIdVal, CryptoExchangeProvider.RevolutX, cancellationToken),
            await GetIbkrHealthAsync(userIdVal, cancellationToken),
        ];

        return await WithConnectionHealthAsync(userIdVal, entries, cancellationToken);
    }

    /// <summary>
    /// For a provider in <see cref="ConnectionHealthOptions.ActiveProviders"/>, the entry is read from the connection health its
    /// module stores (<see cref="IConnectionHealthReader"/>, labelled by <see cref="ConnectionHealthSurface"/>, as the app's
    /// Accounts page is): a failure the policy has not told the user about (Degraded, or one of ours) is not an error. Every
    /// other provider's entry is returned exactly as its own columns give it, with no health field.
    /// </summary>
    private async Task<IReadOnlyList<SyncHealthEntry>> WithConnectionHealthAsync(
        Guid userId, IReadOnlyList<SyncHealthEntry> entries, CancellationToken ct)
    {
        var options = healthOptions.CurrentValue;
        if (!options.ActiveProviders.Any(p => entries.Any(e => IsInstitution(e.Provider, p))))
            return entries;

        List<ConnectionHealthEntry> rows;
        try
        {
            rows = [];
            foreach (var reader in healthReaders)
                rows.AddRange(await reader.GetForUserAsync(userId, ct));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to read connection health for user {UserId}; reporting sync columns only.", userId);
            return entries;
        }

        var active = rows.Where(r => options.IsActive(r.Provider)).ToList();
        return [.. entries.Select(e => ProviderState(active, e.Provider) is { } state ? FromHealth(e, state) : e)];
    }

    private static bool IsInstitution(string entryProvider, string rowProvider) =>
        string.Equals(ConnectionHealthSurface.InstitutionOf(rowProvider), entryProvider, StringComparison.OrdinalIgnoreCase);

    // The provider's state as its Accounts row shows it, from the rows of the listed providers: each account reads its
    // connection's state while the person is told about it, else its own, and one Dormant account stays quiet beside
    // working siblings.
    private static ConnectionHealthState? ProviderState(IReadOnlyList<ConnectionHealthEntry> rows, string provider)
    {
        var mine = rows.Where(r => IsInstitution(provider, r.Provider)).ToList();
        var connections = mine.Where(r => !r.IsAccount).ToDictionary(r => r.Id, r => r.Health.State);
        var accounts = mine.Where(r => r.IsAccount).Select(r => ConnectionHealthSurface.Effective(
            r.ConnectionId is { } id && connections.TryGetValue(id, out var shared) ? shared : null, r.Health.State));

        var withAccounts = mine.Where(r => r.IsAccount && r.ConnectionId is not null).Select(r => r.ConnectionId!.Value).ToHashSet();
        var bare = connections.Where(c => !withAccounts.Contains(c.Key)).Select(c => c.Value);

        return ConnectionHealthSurface.Worst([.. accounts.OfType<ConnectionHealthState>(), .. bare]);
    }

    private static SyncHealthEntry FromHealth(SyncHealthEntry entry, ConnectionHealthState state)
    {
        var health = ConnectionHealthSurface.StatusFor(state) ?? "healthy";
        return state switch
        {
            ConnectionHealthState.ActionRequired => entry with { Status = "error", ErrorMessage = ReauthRequiredMessage, Health = health },
            ConnectionHealthState.Failing => entry with
            {
                Status = "error", ErrorMessage = entry.ErrorMessage ?? RetryingMessage, Health = health,
            },
            ConnectionHealthState.Dormant => entry with
            {
                Status = "error", ErrorMessage = "Failing for days; the connection or account may be closed.", Health = health,
            },
            _ => entry with
            {
                Status = entry.LastSyncAt is null ? "never_synced" : "ok", ErrorMessage = null, Health = health,
            },
        };
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
                .Where(a => a.UserId == userId && a.Provider == "monobank"
                    && (a.SyncStatus == "failed" || a.SyncStatus == ReauthRequiredStatus))
                .Select(a => new { a.SyncStatus, a.LastSyncError })
                .FirstOrDefaultAsync(ct);

            if (failedAccount is not null)
                return new SyncHealthEntry(
                    "monobank", credential.LastSyncAt, "error",
                    failedAccount.LastSyncError ?? (failedAccount.SyncStatus == ReauthRequiredStatus ? ReauthRequiredMessage : null));

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
                .Where(a => a.UserId == userId && a.Provider == "truelayer")
                .Select(a => new { a.SyncStatus, a.LastSyncError, a.UpdatedAt })
                .ToListAsync(ct);

            var syncedAccounts = accounts.Where(a => a.SyncStatus is "active" or "failed").ToList();
            DateTime? lastSyncAt = syncedAccounts.Count > 0 ? syncedAccounts.Max(a => a.UpdatedAt) : null;

            var latestFailed = syncedAccounts
                .Where(a => a.SyncStatus == "failed")
                .OrderByDescending(a => a.UpdatedAt)
                .FirstOrDefault();

            if (latestFailed is not null)
                return new SyncHealthEntry("truelayer", lastSyncAt, "error", latestFailed.LastSyncError);

            // A lapsed consent surfaces as reauth_required accounts and/or an EXPIRED connection;
            // neither is "active" or "failed", so check them explicitly. A connection can expire
            // before its first discovery leaves any account behind, so this precedes never_synced.
            var hasExpiredConnection = await _bankSync.TrueLayerConnections
                .AsNoTracking()
                .AnyAsync(c => c.UserId == userId && c.Status == ExpiredConnectionStatus, ct);

            if (hasExpiredConnection || accounts.Any(a => a.SyncStatus == ReauthRequiredStatus))
                return new SyncHealthEntry("truelayer", lastSyncAt, "error", ReauthRequiredMessage);

            if (accounts.Count == 0 || accounts.All(a => a.SyncStatus == "pending"))
                return new SyncHealthEntry("truelayer", null, "never_synced", null);

            return new SyncHealthEntry("truelayer", lastSyncAt, "ok", null);
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

/// <param name="Health">
/// The connection-health label (<c>healthy</c>, <c>retrying</c>, <c>reconnect_needed</c>, <c>dormant</c>) for a provider
/// the policy drives; omitted for every other provider.
/// </param>
public sealed record SyncHealthEntry(
    string Provider,
    DateTime? LastSyncAt,
    string Status,
    string? ErrorMessage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Health = null);

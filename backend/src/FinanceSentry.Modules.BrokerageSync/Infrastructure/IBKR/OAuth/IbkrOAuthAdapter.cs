using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;

/// <summary>
/// <see cref="IBrokerAdapter"/> backed by IBKR OAuth 1.0a. Loads the user's
/// stored artifacts, resolves them to signing material, and reads the portfolio
/// through <see cref="IbkrOAuthClient"/>. The live session token is cached in
/// the client, so the repeated resolves across one sync are cheap.
/// </summary>
public sealed class IbkrOAuthAdapter(
    IIBKRCredentialRepository credentialRepository,
    IIbkrCredentialResolver resolver,
    IbkrOAuthClient client,
    ILogger<IbkrOAuthAdapter> logger) : IBrokerAdapter
{
    private const string BaseLedgerKey = "BASE";

    public string BrokerName => "IBKR";

    public async Task EnsureSessionAsync(Guid credentialId, CancellationToken ct = default)
    {
        using var credentials = await ResolveAsync(credentialId, ct);
        var accounts = await client.GetAccountsAsync(credentials, ct);
        if (accounts.Count == 0)
            throw new BrokerAuthException(
                "IBKR OAuth session returned no accounts — the consumer key may not be active yet.", "IBKR");
    }

    public async Task<string> GetAccountIdAsync(Guid credentialId, CancellationToken ct = default)
    {
        using var credentials = await ResolveAsync(credentialId, ct);
        var accounts = await client.GetAccountsAsync(credentials, ct);
        if (accounts.Count == 0)
            throw new InvalidOperationException("No IBKR accounts found for the authenticated session.");
        return accounts[0];
    }

    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(
        Guid credentialId, string accountId, CancellationToken ct = default)
    {
        using var credentials = await ResolveAsync(credentialId, ct);
        var positions = await client.GetPositionsAsync(credentials, accountId, ct);
        var result = positions
            .Select(p => new BrokerPosition(
                Symbol: p.ContractDesc,
                InstrumentType: p.AssetClass,
                Quantity: p.Position,
                UsdValue: p.MktValue,
                AverageCostUsd: p.AvgPrice ?? p.AvgCost,
                Conid: p.Conid))
            .ToList();

        result.AddRange(await GetCashPositionsAsync(credentials, accountId, ct));
        return result;
    }

    /// <summary>
    /// Uninvested cash from the IBKR ledger, surfaced as synthetic CASH positions so it shows in
    /// holdings and net worth. Best-effort: the ledger endpoint can 403 on restricted read-only
    /// sessions, and cash is supplementary, so a failure is logged and skipped rather than failing
    /// the whole sync.
    /// </summary>
    private async Task<IReadOnlyList<BrokerPosition>> GetCashPositionsAsync(
        IbkrOAuthCredentials credentials, string accountId, CancellationToken ct)
    {
        try
        {
            var ledger = await client.GetLedgerAsync(credentials, accountId, ct);
            return ledger
                .Where(kv => !string.Equals(kv.Key, BaseLedgerKey, StringComparison.OrdinalIgnoreCase)
                             && kv.Value.CashBalance != 0m)
                .Select(kv =>
                {
                    var currency = string.IsNullOrWhiteSpace(kv.Value.Currency) ? kv.Key : kv.Value.Currency!;
                    return new BrokerPosition(
                        Symbol: $"{currency} Cash",
                        InstrumentType: "CASH",
                        Quantity: kv.Value.CashBalance,
                        UsdValue: CurrencyConverter.ToUsd(kv.Value.CashBalance, currency),
                        AverageCostUsd: null);
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IBKR cash ledger fetch failed for account {AccountId}; skipping cash.", accountId);
            return [];
        }
    }

    private async Task<IbkrOAuthCredentials> ResolveAsync(Guid credentialId, CancellationToken ct)
    {
        var credential = await credentialRepository.GetByIdAsync(credentialId, ct)
            ?? throw new BrokerAuthException(
                $"No IBKR credential found for id {credentialId}.", "IBKR");
        return resolver.Resolve(credential);
    }
}

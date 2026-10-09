using System.ComponentModel;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Mcp.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetAccountSummaryTool(
    IBankingAccountsReader bankingReader,
    ICryptoHoldingsReader cryptoReader,
    IBrokerageHoldingsReader brokerageReader,
    IIdentityResolver identity,
    ILogger<GetAccountSummaryTool> logger)
{
    private readonly IBankingAccountsReader _bankingReader = bankingReader;
    private readonly ICryptoHoldingsReader _cryptoReader = cryptoReader;
    private readonly IBrokerageHoldingsReader _brokerageReader = brokerageReader;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetAccountSummaryTool> _logger = logger;

    [McpServerTool(Name = "get_account_summary")]
    [Description("Returns a consolidated account summary across banking, crypto, and brokerage providers. isCash is true for cash held (non-credit bank accounts, broker cash such as \"USD Cash\", and fiat held on a crypto venue) and false for invested positions and credit accounts, whose balance is the amount owed. Summing the isCash entries gives cash held; the cashUsd of get_portfolio_snapshot also nets owed credit balances.")]
    public async Task<IReadOnlyList<AccountSummaryEntry>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        var results = new List<AccountSummaryEntry>();

        // Banking accounts — each account is one entry.
        try
        {
            var accounts = await _bankingReader.GetAccountSummariesAsync(userIdVal, cancellationToken);
            results.AddRange(accounts.Select(a => new AccountSummaryEntry(
                a.AccountId.ToString(),
                a.BankName,
                a.Provider,
                a.Currency,
                a.CurrentBalance ?? 0m,
                IsCash: !AccountBalanceMath.IsLiability(a.AccountType))));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BankSync provider unavailable for user {UserId}; contributing empty list.", userIdVal);
        }

        // Crypto holdings — each asset is one entry denominated in USD.
        try
        {
            var holdings = await _cryptoReader.GetHoldingsAsync(userIdVal, cancellationToken);
            results.AddRange(holdings.Select(h => new AccountSummaryEntry(
                h.Asset,
                h.Asset,
                h.Provider,
                "USD",
                h.UsdValue,
                IsCash: h.IsVenueFiat)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CryptoSync provider unavailable for user {UserId}; contributing empty list.", userIdVal);
        }

        // Brokerage positions — each position is one entry denominated in USD.
        try
        {
            var positions = await _brokerageReader.GetHoldingsAsync(userIdVal, cancellationToken);
            results.AddRange(positions.Select(h => new AccountSummaryEntry(
                h.Symbol,
                h.Symbol,
                h.Provider,
                "USD",
                h.UsdValue,
                IsCash: AssetClassNormalizer.Normalize(h.InstrumentType) == AssetClassNormalizer.Cash)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BrokerageSync provider unavailable for user {UserId}; contributing empty list.", userIdVal);
        }

        return results;
    }
}

public sealed record AccountSummaryEntry(
    string AccountId,
    string Name,
    string Provider,
    string Currency,
    decimal Balance,
    bool IsCash = false);

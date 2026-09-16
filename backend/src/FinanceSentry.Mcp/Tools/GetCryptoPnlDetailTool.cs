using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.CryptoSync.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetCryptoPnlDetailTool(
    IQueryHandler<GetCryptoPnlDetailQuery, CryptoPnlDetailResponse> pnlHandler,
    IIdentityResolver identity,
    ILogger<GetCryptoPnlDetailTool> logger)
{
    private readonly IQueryHandler<GetCryptoPnlDetailQuery, CryptoPnlDetailResponse> _pnlHandler = pnlHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetCryptoPnlDetailTool> _logger = logger;

    [McpServerTool(Name = "get_crypto_pnl_detail")]
    [Description("Returns per-asset crypto P&L across connected venues (Binance, Revolut X); each item names its provider. Cost basis and realized P&L are derived from Binance trade history (USDT-paired). Cost basis is null for assets that have never been traded via a USD-stablecoin pair (e.g. airdrops, transfers) and for Revolut X holdings, whose trade history is not ingested yet (history before connect is unrecoverable). Defaults to the authenticated MCP identity when userId is omitted.")]
    public async Task<IReadOnlyList<CryptoPnlAssetEntry>> ExecuteAsync(
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        CryptoPnlDetailResponse response;
        try
        {
            response = await _pnlHandler.Handle(new GetCryptoPnlDetailQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Crypto P&L query unavailable for user {UserId}; returning empty list.", userIdVal);
            return [];
        }

        return response.Items
            .Select(i => new CryptoPnlAssetEntry(
                i.Asset,
                i.Quantity,
                i.CurrentValueUsd,
                i.CostBasisUsd,
                i.AverageBuyPriceUsd,
                i.UnrealizedPnlUsd,
                i.UnrealizedPnlPercent,
                i.RealizedPnlUsd,
                i.LastTradeAt,
                i.TradeCount,
                i.Provider))
            .ToList();
    }
}

public sealed record CryptoPnlAssetEntry(
    string Asset,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? CostBasisUsd,
    decimal? AverageBuyPriceUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    decimal? RealizedPnlUsd,
    DateTime? LastTradeAt,
    int TradeCount,
    string Provider);

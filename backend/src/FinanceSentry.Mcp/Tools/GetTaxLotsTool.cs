using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetTaxLotsTool(
    IQueryHandler<GetTaxLotsQuery, TaxLotsResponse> taxLotsHandler,
    IIdentityResolver identity,
    ILogger<GetTaxLotsTool> logger)
{
    private readonly IQueryHandler<GetTaxLotsQuery, TaxLotsResponse> _taxLotsHandler = taxLotsHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetTaxLotsTool> _logger = logger;

    [McpServerTool(Name = "get_tax_lots")]
    [Description("Returns brokerage tax lots — one lot per current position. basisState is \"Verified\", \"Unverified\" or \"Unknown\" (fs-688): cost basis is independently recomputed from persisted fills and reconciled against the stored figure. Whenever basisState is not \"Verified\" — no fill history covers the full held quantity, or the recomputed cost basis disagrees with the stored one — averageCostUsd, costBasisUsd, unrealizedPnlUsd and unrealizedPnlPercent are null. Do not state or infer gain/loss for a lot whose basisState is not \"Verified\". Defaults to the authenticated MCP identity when userId is omitted.")]
    public async Task<IReadOnlyList<TaxLotEntry>> ExecuteAsync(
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        TaxLotsResponse response;
        try
        {
            response = await _taxLotsHandler.Handle(new GetTaxLotsQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tax lots query unavailable for user {UserId}; returning empty list.", userIdVal);
            return [];
        }

        return response.Items
            .Select(i => new TaxLotEntry(
                i.Symbol,
                i.InstrumentType,
                i.Quantity,
                i.CurrentValueUsd,
                i.AverageCostUsd,
                i.CostBasisUsd,
                i.UnrealizedPnlUsd,
                i.UnrealizedPnlPercent,
                i.AcquiredAt,
                i.IsLongTerm,
                response.Provider,
                i.BasisState))
            .ToList();
    }
}

public sealed record TaxLotEntry(
    string Symbol,
    string InstrumentType,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? AverageCostUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    DateTime? AcquiredAt,
    bool IsLongTerm,
    string Provider,
    string BasisState);

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetPortfolioGapTool(
    IQueryHandler<GetGapModeQuery, GapModeResult> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_portfolio_gap")]
    [Description("Portfolio-construction gap mode: the owner's current book against the target the constructor builds for an archetype ('growth' or 'balanced'; default growth), as \"missing X, overweight Y\". The target is the universe screen's own construction (coverage set plus held and watchlist names, the owner's IPS sleeves, exclusions and risk limits, rules only, no model) so the two never disagree. Returns bookValueUsd (the denominator of every percentage, cash included), summary (headline text and the missing, underweight and overweight names, largest gap first), sleeves[] (asset class: current vs target), sectors[] (the equities names grouped by their stored sector, 'Unclassified' when the source gave none) and names[] (symbol, sector, status Missing / Underweight / OnTarget / Overweight, heldOutsideTarget, currentPct / targetPct / gapPct and currentUsd / targetUsd / gapUsd, and the construction's reason). A gap is target minus current: positive is a shortfall, negative an excess. Percentages are rounded to 0.01 point and USD to the cent. hasBook=false means the book has no value to size against: percentages only, USD amounts null. refusal is set when the owner's IPS cannot be built as written, and no gap is then given. Always carries a label: this is informational, not a recommendation to buy or sell, and nothing here places or prepares a trade. Read-only; unknown archetype names error.")]
    public async Task<GapModeResult?> ExecuteAsync(
        [Description("Archetype key, 'growth' or 'balanced'. Omit for the configured default.")] string? archetype = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetGapModeQuery(effective.Value, archetype), cancellationToken);
    }
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetMarketRegimeTool(
    IQueryHandler<GetMarketRegimeQuery, RegimeStateDto> handler)
{
    [McpServerTool(Name = "get_market_regime")]
    [Description("Returns the current market regime on TWO orthogonal, evidence-backed macro axes (never merged into one label): (1) volatility from the VIX — band Calm/Normal/Stressed/Panic plus level, 20-day SMA, and trend; (2) rates from the FRED 10y-2y treasury spread — band Steep/Normal/Flat/Inverted plus the raw yields, spread, a recession-warning flag, and a growth-vs-value tilt hint. Each axis reports its last band-change date. An unavailable axis (e.g. FRED keyless) is reported as available:false with a null band — never a fabricated regime. Regime is CONTEXT only — it never triggers buy/sell/cash actions. Reads the persisted latest reading; never triggers a fetch.")]
    public async Task<RegimeStateDto> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetMarketRegimeQuery(), cancellationToken);
    }
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetDcfValuationTool(
    IQueryHandler<GetDcfValuationQuery, DcfValuationResult> handler)
{
    [McpServerTool(Name = "get_dcf_valuation")]
    [Description("Intrinsic value per share for one EQUITY ticker from a five-year unlevered free-cash-flow DCF, with a sensitivity range and the revenue growth the current price implies. Method: base year = latest annual period (trailing twelve months of revenue when the annual is stale, flag BASE_YEAR_TTM); year-1 growth starts at the historical revenue CAGR (capped at 25%, flag GROWTH_CAPPED) and fades in a straight line to the terminal rate; operating margin, D&A, capex and working-capital ratios stay at their 3-year averages; FCF = EBIT x (1 - tax) + D&A - capex - change in NWC. WACC = CAPM cost of equity (10-year Treasury via ^TNX + beta x 5% equity risk premium) and after-tax cost of debt weighted by market equity and net debt (net cash: WACC = cost of equity). Mid-year discounting; Gordon terminal value on the year after the last, capped at the lower of the risk-free rate and WACC - 1pt (TERMINAL_GROWTH_CAPPED) and REFUSED when growth >= WACC. Equity = EV - (debt - cash), over shares outstanding, converted to the quote currency (FX_CONVERTED / FX_UNKNOWN). summary: valuePerShare vs price, upsidePct, rangeLow/rangeHigh (min/max of the WACC x terminal-growth grid), impliedGrowthPct (reverse DCF: the constant 5-year revenue growth that makes value equal the price, margins and WACC held) next to historicalCagrPct, terminalShareOfEvPct (flag TERMINAL_HEAVY above 75%). sensitivity: two 5x5 grids of full recomputes (WACC x terminal growth; year-1 growth x operating margin), centre cell = the headline. exitMultipleCrossCheck: the terminal value redone as final-year EBITDA x the peer-median EV/EBITDA from get_comps_valuation. Fallbacks are flagged, never silent: TAX_RATE_DEFAULTED (21%), COST_OF_DEBT_DEFAULTED, NWC_ASSUMED_ZERO, WACC_FLOORED (a computed WACC below 1%, e.g. from a zero or negative beta, is raised to 1%). Rates in assumptions and grid axes are fractions (0.09 = 9%); the Pct fields are percentages. Overrides (fractions) replace year-1 growth, operating margin, WACC, terminal growth or the projection length for this call (OVERRIDES_APPLIED); assumptions echoes what was used. Missing data is null with a typed notEvaluableReason — NEVER zero-filled: banks and insurers FINANCIAL_SECTOR, NO_OPERATING_PROFIT, INSUFFICIENT_HISTORY, CASH_FLOW_UNAVAILABLE, BETA_UNAVAILABLE / RISK_FREE_UNAVAILABLE (unless wacc is overridden), TERMINAL_GROWTH_NOT_BELOW_WACC, NEGATIVE_EQUITY_VALUE, INVALID_OVERRIDE, and the fundamentals chain's own coverage status when it is not covered. Omits minority interest, leases and pensions. Reply from summary and flags; the grids are for follow-up questions. Computed on read from Yahoo (no stored row, no LLM). Non-equity tickers (crypto, ETFs) return notApplicable=true.")]
    public async Task<DcfValuationResult> ExecuteAsync(
        [Description("Equity ticker (e.g. MCD). Non-equity tickers return an explicit not-applicable result.")] string ticker,
        [Description("Optional year-1 revenue growth as a fraction (0.08 = 8%); fades to the terminal rate. Default: the historical CAGR, capped at 25%.")] decimal? growth = null,
        [Description("Optional operating margin as a fraction (0.20 = 20%), held across the projection. Default: the 3-year average.")] decimal? operatingMargin = null,
        [Description("Optional WACC as a fraction (0.09 = 9%). Default: CAPM + market-value weights.")] decimal? wacc = null,
        [Description("Optional terminal growth as a fraction (0.025 = 2.5%); must stay below WACC. Default 2.5%, capped.")] decimal? terminalGrowth = null,
        [Description("Optional projection length in years, 3 to 10. Default 5.")] int? years = null,
        CancellationToken cancellationToken = default)
        => await handler.Handle(
            new GetDcfValuationQuery(ticker, growth, operatingMargin, wacc, terminalGrowth, years),
            cancellationToken);
}

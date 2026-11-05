using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Tax;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetDisposalTaxContextTool(
    IQueryHandler<GetDisposalTaxContextQuery, DisposalTaxContext?> handler,
    IIdentityResolver identity,
    ILogger<GetDisposalTaxContextTool> logger)
{
    private readonly IQueryHandler<GetDisposalTaxContextQuery, DisposalTaxContext?> _handler = handler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetDisposalTaxContextTool> _logger = logger;

    [McpServerTool(Name = "get_disposal_tax_context")]
    [Description("Returns, for one held symbol, the Irish disposal-tax context of selling the whole position today: the regime (regime \"cgt\" for an ordinary share, \"exitTax\" for an offshore fund/ETF, set by the person's instrument classification, never inferred), the tax year's rate (and annualExemptionEur for CGT, which all of the year's disposals share), and per FIFO lot the acquisition date, euro cost, euro proceeds and gain. All amounts are EUR: every cost is converted on its own purchase date and the proceeds at today's rate. estimatedTaxEur is before the CGT annual exemption; taxAfterExemptionEur assumes this is the year's only CGT disposal. For exitTax, deemedDisposalOn is each lot's 8-year deemed-disposal date and nextDeemedDisposalOn the nearest one. status is \"withheld\" (no figures) unless the position is held at IBKR, its fills explain the whole holding, the instrument is classified and the cost basis is Verified; withheldReasons then lists PROVIDER_NOT_SUPPORTED, PARTIAL_COVERAGE, UNCLASSIFIED, BASIS_NOT_VERIFIED, FOUR_WEEK_RULE (an ordinary share bought and sold within 28 days of each other, where Irish matching rules replace FIFO) or DEEMED_DISPOSAL_PASSED (a fund lot older than 8 years). Do not estimate tax yourself for a withheld position. ratesCarriedForward means the year has no reviewed rate row yet and the latest earlier year's rates were used. Returns null when the symbol is not held. Context for the person's own planning, never a trading signal, tax advice or a tax return: refer them to Revenue or a tax adviser, and do not turn it into buy, sell or hold calls.")]
    public async Task<DisposalTaxContext?> ExecuteAsync(
        [Description("The held symbol, e.g. \"VUAA\".")] string symbol,
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null || string.IsNullOrWhiteSpace(symbol)) return null;
        var userIdVal = effective.Value;

        try
        {
            return await _handler.Handle(new GetDisposalTaxContextQuery(userIdVal, symbol), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disposal tax context unavailable for user {UserId}; returning null.", userIdVal);
            return null;
        }
    }
}

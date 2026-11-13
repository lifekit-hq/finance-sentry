using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetReferenceUniverseTool(IQueryHandler<GetReferenceUniverseQuery, ReferenceUniverseView> handler)
{
    [McpServerTool(Name = "get_reference_universe")]
    [Description("Reads the reference universe the discovery funnel draws from: the S&P 500 (list 'sp500'), the Nasdaq-100 ('nasdaq100'), sector ('sector:Energy') and industry ('industry:Semiconductors') lists from FinanceDatabase, and the theme lists declared in configuration ('theme:memory', 'theme:ai-infra', 'theme:power', 'theme:semiconductors', 'theme:energy'). With no arguments it returns the catalogue of lists (key, kind, source, memberCount) and each dataset's freshness. With 'list' it returns that list's members (ticker, name; capped by 'limit', default 200). With 'ticker' it returns every list the ticker belongs to. 'sources' always reports per dataset: lastSuccessAt, lastFailureAt, lastError, consecutiveFailures and 'stale'; a stale or failing dataset serves its last good refresh, so check it before treating a list as current. Shared market reference data, not the owner's holdings.")]
    public async Task<ReferenceUniverseView> ExecuteAsync(
        [Description("Optional list key, matched case-insensitively, e.g. 'sp500', 'theme:memory', 'sector:Information Technology'.")] string? list = null,
        [Description("Optional ticker; returns the lists it belongs to. Takes precedence over 'list'.")] string? ticker = null,
        [Description("Optional cap on the members returned for 'list' (1-1000, default 200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetReferenceUniverseQuery(list, ticker, limit), cancellationToken);
    }
}

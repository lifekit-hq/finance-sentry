using System.ComponentModel;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetVerdictCoverageAuditTool(
    IQueryHandler<GetVerdictCoverageAuditQuery, VerdictCoverageAudit> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_verdict_coverage_audit")]
    [Authorize(Policy = AuthPolicies.RequireAiUse)]
    [Description("Read-only audit of why a held, watched, thesis or coverage symbol has or lacks a verdict. One row per symbol (held names first, capped at 100) with the origin lists it came from, what the holdings feed knows (provider, instrument type, asset class, listing exchange when a Flex statement reported it), whether the Radar universe takes it, and the state of each section a verdict reads: structure (bars), fundamentals, valuation, peers, ETF look-through and the verdict itself (word, fired rule, wait reason). Every section carries a gap: SourceMissing, OutsideUniverse, SymbolMapping, FeedLimit, NotApplicable, NoHoldings, Partial, ScorecardThrew or SectionThrew (a thrown read names only the exception type), or null when it was read. Computed at request time from the owner's holdings and stored nowhere; it reports what the verdict could see and never recommends or acts on a price move. Pass includeDossier=true to also name the dossier sections whose read threw (SectionThrew); that pass runs the whole dossier per symbol, so it covers only the first 20 rows and dossierSkipped counts the rest. Off by default. Needs the owner's ai.use permission.")]
    public async Task<VerdictCoverageAudit?> ExecuteAsync(
        [Description("Also read the dossier sections of the first 20 rows (default false).")] bool includeDossier = false,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetVerdictCoverageAuditQuery(effective.Value, includeDossier), cancellationToken);
    }
}

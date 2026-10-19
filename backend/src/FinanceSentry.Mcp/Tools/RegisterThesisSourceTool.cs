using System.ComponentModel;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RegisterThesisSourceTool(
    ICommandHandler<RegisterThesisSourceCommand, RegisteredSourceDto> handler)
{
    [Authorize(Policy = AuthPolicies.RequireMcpPersonalToken)]
    [McpServerTool(Name = "register_thesis_source")]
    [Description("Registers an external news source (RSS feed or scrapeable page) so it is ingested on the normal cadence, optionally attached to a thesis so its articles are tagged to it (e.g. TrendForce → the DRAM thesis). Registering is idempotent by URL — re-registering an existing URL updates and re-enables it. Registering a source is a change to what Denys is monitored on: it is his deliberate decision, so only his own session can call this (a service token such as Ledger's is refused) and it MUST have his explicit confirmation — never register a source on your own initiative. Omitting thesisId registers a MARKET-WIDE source (affects all monitoring), which especially requires his confirmation.")]
    public async Task<RegisteredSourceDto> ExecuteAsync(
        [Description("Display name, e.g. 'TrendForce Press Center'.")] string name,
        [Description("Absolute feed or page URL.")] string url,
        [Description("Source kind: 'Rss' for a feed, 'Page' for a scrapeable HTML page (must have a registered page scraper).")] string kind,
        [Description("Optional thesis GUID to attach the source to. Omit/null registers a market-wide source (requires Denys's confirmation).")] Guid? thesisId = null,
        [Description("Optional keyword filters — only articles whose title/summary match a keyword are tagged to the thesis.")] string[]? keywords = null,
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(
            new RegisterThesisSourceCommand(thesisId, name, url, kind, keywords), cancellationToken);
    }
}

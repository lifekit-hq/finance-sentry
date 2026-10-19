using System.ComponentModel;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Responses;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class WatchlistTool(
    IQueryHandler<GetWatchlistQuery, IReadOnlyList<WatchlistItemDto>> listHandler,
    ICommandHandler<AddWatchlistItemCommand, WatchlistItemDto> addHandler,
    ICommandHandler<RemoveWatchlistItemCommand, bool> removeHandler,
    IIdentityResolver identity,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorization)
{
    private const string ServiceTokenRefusal =
        "a service token can only list the watchlist; adding or removing an entry is the owner's own decision.";

    [McpServerTool(Name = "watchlist")]
    [Description(
        "Manage the caller's watchlist (tickers tracked, not necessarily held). "
        + "action=list returns all entries; action=add requires ticker (exchange/note optional); "
        + "action=remove requires itemId (from a prior list/add). Scoped to the authenticated MCP identity. "
        + "A service token (Ledger) can only list: add and remove need the owner's own session.")]
    public async Task<WatchlistToolResult?> ExecuteAsync(
        [Description("What to do: list | add | remove.")] string action,
        [Description("Ticker to add, e.g. AAPL, NVDA, BTC-USD. Required for action=add.")] string? ticker = null,
        [Description("Optional exchange code for action=add, e.g. NASDAQ, NYSE.")] string? exchange = null,
        [Description("Optional free-form note for action=add.")] string? note = null,
        [Description("Watchlist item id to remove (from list/add). Required for action=remove.")] Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        switch (action?.Trim().ToLowerInvariant())
        {
            case "list":
                var items = await listHandler.Handle(new GetWatchlistQuery(effective.Value), cancellationToken);
                return WatchlistToolResult.ForList(items);

            case "add":
                if (!await MayChangeWatchlistAsync())
                {
                    return WatchlistToolResult.Invalid("add", ServiceTokenRefusal);
                }

                if (string.IsNullOrWhiteSpace(ticker))
                {
                    return WatchlistToolResult.Invalid("add", "action=add requires a ticker.");
                }

                var added = await addHandler.Handle(
                    new AddWatchlistItemCommand(effective.Value, ticker, exchange, note), cancellationToken);
                return WatchlistToolResult.ForAdd(added);

            case "remove":
                if (!await MayChangeWatchlistAsync())
                {
                    return WatchlistToolResult.Invalid("remove", ServiceTokenRefusal);
                }

                if (itemId is null)
                {
                    return WatchlistToolResult.Invalid("remove", "action=remove requires an itemId.");
                }

                var removed = await removeHandler.Handle(
                    new RemoveWatchlistItemCommand(effective.Value, itemId.Value), cancellationToken);
                return WatchlistToolResult.ForRemove(removed);

            default:
                return WatchlistToolResult.Invalid(action ?? "", "action must be one of: list, add, remove.");
        }
    }

    /// <summary>
    /// The same gate the Research write tools carry as an attribute (<see cref="AuthPolicies.RequireMcpPersonalToken"/>),
    /// applied per action because list stays open to every caller. stdio has no HTTP request: it is the owner's own
    /// local session.
    /// </summary>
    private async Task<bool> MayChangeWatchlistAsync()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user is null
            || (await authorization.AuthorizeAsync(user, AuthPolicies.RequireMcpPersonalToken)).Succeeded;
    }
}

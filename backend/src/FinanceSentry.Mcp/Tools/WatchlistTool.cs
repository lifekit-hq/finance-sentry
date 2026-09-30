using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Responses;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class WatchlistTool(
    IQueryHandler<GetWatchlistQuery, IReadOnlyList<WatchlistItemDto>> listHandler,
    ICommandHandler<AddWatchlistItemCommand, WatchlistItemDto> addHandler,
    ICommandHandler<RemoveWatchlistItemCommand, bool> removeHandler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "watchlist")]
    [Description(
        "Manage the caller's watchlist (tickers tracked, not necessarily held). "
        + "action=list returns all entries; action=add requires ticker (exchange/note optional); "
        + "action=remove requires itemId (from a prior list/add). Scoped to the authenticated MCP identity.")]
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
                if (string.IsNullOrWhiteSpace(ticker))
                {
                    return WatchlistToolResult.Invalid("add", "action=add requires a ticker.");
                }

                var added = await addHandler.Handle(
                    new AddWatchlistItemCommand(effective.Value, ticker, exchange, note), cancellationToken);
                return WatchlistToolResult.ForAdd(added);

            case "remove":
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
}

using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Responses;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Domain.Exceptions;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class CommittedMerchantsTool(
    IQueryHandler<ListCommittedMerchantPinsQuery, IReadOnlyList<CommittedMerchantPinDto>> listHandler,
    ICommandHandler<PinCommittedMerchantCommand, PinCommittedMerchantResult> pinHandler,
    ICommandHandler<UnpinCommittedMerchantCommand, bool> unpinHandler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "committed_merchants")]
    [Description(
        "Manage the merchants the caller declared as committed spending — obligations only the "
        + "user knows about (a standing payment to a person, a gym lock-in), which no recurrence "
        + "detector or category rule can see. Pinned merchants count as committed rather than "
        + "discretionary in every cash-flow split. action=list returns all pins; action=pin "
        + "requires merchant; action=unpin takes either the same merchant text or the "
        + "merchantKey a listing returned. A pin matches a statement line by normalized merchant "
        + "name, exactly rather than as a substring, so it does not claim rows that name the "
        + "merchant only inside a longer description. Scoped to the authenticated MCP identity.")]
    public async Task<CommittedMerchantsToolResult?> ExecuteAsync(
        [Description("What to do: list | pin | unpin.")] string action,
        [Description("Merchant as it appears on the statement, e.g. 'Mario Scalas', 'Anytime Fitness'. Required for pin and unpin.")] string? merchant = null,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var normalizedAction = action?.Trim().ToLowerInvariant();
        if (normalizedAction is not ("list" or "pin" or "unpin"))
        {
            return CommittedMerchantsToolResult.Invalid(action ?? "", "action must be one of: list, pin, unpin.");
        }

        if (normalizedAction == "list")
        {
            var pins = await listHandler.Handle(
                new ListCommittedMerchantPinsQuery(effective.Value), cancellationToken);
            return CommittedMerchantsToolResult.ForList(pins);
        }

        if (string.IsNullOrWhiteSpace(merchant))
        {
            return CommittedMerchantsToolResult.Invalid(
                normalizedAction, $"action={normalizedAction} requires a merchant.");
        }

        try
        {
            if (normalizedAction == "pin")
            {
                var result = await pinHandler.Handle(
                    new PinCommittedMerchantCommand(effective.Value, merchant), cancellationToken);
                return CommittedMerchantsToolResult.ForPin(result.Pin, result.AlreadyPinned);
            }

            var unpinned = await unpinHandler.Handle(
                new UnpinCommittedMerchantCommand(effective.Value, merchant), cancellationToken);
            return CommittedMerchantsToolResult.ForUnpin(unpinned);
        }
        catch (UnpinnableMerchantException ex)
        {
            // The merchant text normalizes to nothing nameable. Reported in the result rather
            // than thrown: an MCP caller gets a message it can act on instead of a transport
            // error it cannot read.
            return CommittedMerchantsToolResult.Invalid(normalizedAction, ex.Message);
        }
    }
}

using System.ComponentModel;
using System.Globalization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Queries;
using FinanceSentry.Modules.Companion.Domain;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListLedgerCorrectionsTool(
    IQueryHandler<ListLedgerCorrectionsQuery, LedgerCorrectionsResult> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_ledger_corrections")]
    [Description("Read-only. Lists the Ledger correction log for review: the corrections recorded with record_ledger_correction in one calendar month (UTC), newest first, with that month's count and the count for every month that has corrections. Each item carries what Ledger said, what was wrong, the user's correction, when it was made and when it was recorded. Empty is honest emptiness. Returns null for an unparseable month.")]
    public async Task<LedgerCorrectionsResult?> ExecuteAsync(
        [Description("Calendar month as yyyy-MM (UTC), e.g. 2026-10. Omit for the current month.")] string? month = null,
        [Description("Max items, default 50, max 200. The counts are never capped.")] int limit = LedgerCorrectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        DateTime start;
        if (string.IsNullOrWhiteSpace(month))
        {
            start = DateTime.UtcNow;
        }
        else if (!DateTime.TryParseExact(month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
        {
            return null;
        }

        var cappedLimit = limit <= 0 ? LedgerCorrectionLimits.DefaultListLimit : Math.Min(limit, LedgerCorrectionLimits.MaxListLimit);
        return await handler.Handle(
            new ListLedgerCorrectionsQuery(effective.Value, start.Year, start.Month, cappedLimit), cancellationToken);
    }
}

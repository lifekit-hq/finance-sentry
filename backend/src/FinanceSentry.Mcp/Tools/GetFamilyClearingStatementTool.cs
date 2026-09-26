using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.BankSync.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetFamilyClearingStatementTool(
    IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement> statementHandler,
    IIdentityResolver identity,
    ILogger<GetFamilyClearingStatementTool> logger)
{
    private readonly IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement> _statementHandler = statementHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetFamilyClearingStatementTool> _logger = logger;

    [McpServerTool(Name = "get_family_clearing_statement")]
    [Description("Returns the family clearing statement for one calendar month: per-counterparty gross received/sent with a presentational net, native per-currency subtotals, and the month's family-support total. Defaults to the authenticated MCP identity when userId is omitted.")]
    public async Task<FamilyClearingStatement> ExecuteAsync(
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        [Description("Calendar month in yyyy-MM format. Defaults to the last complete calendar month.")] string? month = null,
        [Description("How many trailing months of classification to draw on. Defaults to 6.")] int months = 6,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? _identity.GetUserId();
        if (effective is null) return EmptyStatement(month);
        var userIdVal = effective.Value;

        try
        {
            return await _statementHandler.Handle(
                new GetFamilyClearingStatementQuery(userIdVal, month, months),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Family clearing statement query unavailable for user {UserId}; returning empty statement.", userIdVal);
            return EmptyStatement(month);
        }
    }

    private static FamilyClearingStatement EmptyStatement(string? month) =>
        new(month ?? string.Empty, [], SupportTotalUsd: 0m, ReceivedTotalUsd: 0m, ExcludedRoutingLegs: 0);
}

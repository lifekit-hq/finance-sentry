using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Subscriptions.API.Responses;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListSubscriptionsTool(
    IQueryHandler<GetSubscriptionsQuery, SubscriptionsListResponse> subscriptionsHandler,
    IIdentityResolver identity,
    ILogger<ListSubscriptionsTool> logger)
{
    private readonly IQueryHandler<GetSubscriptionsQuery, SubscriptionsListResponse> _subscriptionsHandler = subscriptionsHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<ListSubscriptionsTool> _logger = logger;

    [McpServerTool(Name = "list_subscriptions")]
    [Description("Returns detected recurring charges (subscriptions), excluding dismissed ones.")]
    public async Task<IReadOnlyList<SubscriptionEntry>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        SubscriptionsListResponse response;
        try
        {
            response = await _subscriptionsHandler.Handle(
                new GetSubscriptionsQuery(userIdVal.ToString(), IncludeDismissed: false),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscriptions query unavailable for user {UserId}; returning empty list.", userIdVal);
            return [];
        }

        return response.Items
            .Select(s => new SubscriptionEntry(
                s.Id.ToString(),
                s.MerchantName,
                s.MonthlyEquivalent,
                s.Currency,
                s.LastChargeDate,
                s.DetectedAt))
            .ToList();
    }
}

public sealed record SubscriptionEntry(
    string SubscriptionId,
    string Merchant,
    decimal EstimatedMonthlyAmount,
    string Currency,
    DateOnly LastChargedAt,
    DateTimeOffset DetectedAt);

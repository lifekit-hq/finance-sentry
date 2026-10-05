namespace FinanceSentry.Modules.Subscriptions.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Subscriptions.API.Responses;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

public record GetSubscriptionsQuery(string UserId, bool IncludeDismissed) : IQuery<SubscriptionsListResponse>;

public class GetSubscriptionsQueryHandler(IDetectedSubscriptionRepository repository)
    : IQueryHandler<GetSubscriptionsQuery, SubscriptionsListResponse>
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task<SubscriptionsListResponse> Handle(
        GetSubscriptionsQuery request, CancellationToken cancellationToken)
    {
        var items = await _repository.GetByUserIdAsync(
            request.UserId, request.IncludeDismissed, cancellationToken);

        var active = await _repository.GetActiveByUserIdAsync(request.UserId, cancellationToken);
        var hasInsufficientHistory = active.Count == 0 && items.Count == 0;

        var dtos = items.Select(s => new SubscriptionDto(
            s.Id,
            s.MerchantNameDisplay,
            s.Cadence,
            s.AverageAmount,
            s.LastKnownAmount,
            s.Cadence == "annual" ? s.AverageAmount / 12m : s.AverageAmount,
            s.Currency,
            s.LastChargeDate,
            s.NextExpectedDate,
            s.Status,
            s.OccurrenceCount,
            s.Category,
            s.DetectedAt,
            s.Kind,
            s.TermCount,
            s.EndDate,
            s.StartDate,
            s.RemainingPayments,
            s.IsManual,
            s.IsTracked)).ToList();

        return new SubscriptionsListResponse(dtos, dtos.Count, hasInsufficientHistory);
    }
}

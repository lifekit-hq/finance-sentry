namespace FinanceSentry.Modules.Subscriptions.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.API.Responses;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

public record GetSubscriptionsQuery(string UserId, bool IncludeDismissed) : IQuery<SubscriptionsListResponse>;

public class GetSubscriptionsQueryHandler(
    IDetectedSubscriptionRepository repository,
    IMerchantBrandCatalog brands,
    IMerchantIconService icons)
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
        var priceChanges = await _repository.GetLatestPriceChangesAsync(request.UserId, cancellationToken);

        // A merchant name only ever picks a curated row; a host whose last fetch found nothing is not offered.
        var matched = items.ToDictionary(s => s.Id, s => brands.Match(s.MerchantNameDisplay));
        var missing = await icons.GetKnownMissingAsync(
            matched.Values.OfType<Domain.MerchantBrand>().Select(b => b.Domain).Distinct().ToList(), cancellationToken);

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
            s.IsTracked,
            priceChanges.TryGetValue(s.Id, out var change)
                ? new SubscriptionPriceChangeDto(change.OldAmount, change.NewAmount, change.ChangePercent, change.ChargeDate)
                : null,
            matched[s.Id] is { } brand && !missing.Contains(brand.Domain) ? brand.Id : null)).ToList();

        return new SubscriptionsListResponse(dtos, dtos.Count, hasInsufficientHistory);
    }
}

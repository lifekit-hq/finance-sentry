namespace FinanceSentry.Modules.Companion.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Domain.Repositories;

public record ListPushSubscriptionsQuery(Guid UserId) : IQuery<IReadOnlyList<PushSubscriptionDto>>;

public class ListPushSubscriptionsQueryHandler(IPushSubscriptionRepository subscriptions)
    : IQueryHandler<ListPushSubscriptionsQuery, IReadOnlyList<PushSubscriptionDto>>
{
    public async Task<IReadOnlyList<PushSubscriptionDto>> Handle(ListPushSubscriptionsQuery query, CancellationToken ct)
        => (await subscriptions.ListAsync(query.UserId, ct))
            .Select(s => new PushSubscriptionDto(s.Id, s.DeviceLabel, s.CreatedAt, s.LastSuccessAt, s.DisabledAt is not null))
            .ToList();
}

public record GetPushPreferencesQuery(Guid UserId) : IQuery<PushPreferencesDto>;

public class GetPushPreferencesQueryHandler(INotificationSettingRepository settings)
    : IQueryHandler<GetPushPreferencesQuery, PushPreferencesDto>
{
    public async Task<PushPreferencesDto> Handle(GetPushPreferencesQuery query, CancellationToken ct)
        => new((await settings.GetOrDefaultAsync(query.UserId, ct)).PushEnabled);
}

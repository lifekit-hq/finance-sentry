namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// The merchants the user pinned as committed — the visible half of rule (d), so the reader can
/// see which part of their committed share they declared themselves.
/// </summary>
public sealed record ListCommittedMerchantPinsQuery(Guid UserId)
    : IQuery<IReadOnlyList<CommittedMerchantPinDto>>;

public sealed class ListCommittedMerchantPinsQueryHandler(ICommittedMerchantPinRepository pins)
    : IQueryHandler<ListCommittedMerchantPinsQuery, IReadOnlyList<CommittedMerchantPinDto>>
{
    private readonly ICommittedMerchantPinRepository _pins =
        pins ?? throw new ArgumentNullException(nameof(pins));

    public async Task<IReadOnlyList<CommittedMerchantPinDto>> Handle(
        ListCommittedMerchantPinsQuery query, CancellationToken ct)
    {
        var stored = await _pins.ListAsync(query.UserId, ct);

        return [.. stored.Select(p =>
            new CommittedMerchantPinDto(p.Id, p.MerchantKey, p.DisplayName, p.CreatedAt))];
    }
}

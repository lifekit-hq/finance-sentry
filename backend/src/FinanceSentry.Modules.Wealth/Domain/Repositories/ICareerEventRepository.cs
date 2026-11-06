namespace FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>The non-sensitive columns of a career event: readable on a host that holds no encryption key.</summary>
public sealed record CareerEventOutline(
    Guid Id, CareerEventKind Kind, DateOnly Date, DateOnly? EndDate, string Currency, DateTimeOffset? RetiredAt)
{
    public bool IsCounted(DateOnly today) => CareerEvent.IsCounted(Kind, Date, EndDate, RetiredAt, today);
}

public interface ICareerEventRepository
{
    /// <summary>Whether this host can read and write the encrypted fields (<c>Label</c>, <c>Amount</c>, <c>Note</c>).</summary>
    bool CanReadEncryptedFields { get; }

    /// <summary>The request's own person, in date order. Runs under the Owner filter.</summary>
    Task<IReadOnlyList<CareerEvent>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Never touches an encrypted column, so it works on a host without the key.</summary>
    Task<IReadOnlyList<CareerEventOutline>> ListOutlinesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="ListAsync"/> but for a reader that may run with no principal (the monthly brief job reads a projection):
    /// opts out of the Owner filter and keeps its own <c>UserId</c> predicate.
    /// </summary>
    Task<IReadOnlyList<CareerEvent>> ListUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The unscoped counterpart of <see cref="ListOutlinesAsync"/>.</summary>
    Task<IReadOnlyList<CareerEventOutline>> ListOutlinesUnscopedAsync(Guid userId, CancellationToken ct = default);

    Task<CareerEvent?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task AddAsync(CareerEvent career, CancellationToken ct = default);
    Task RemoveAsync(CareerEvent career, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

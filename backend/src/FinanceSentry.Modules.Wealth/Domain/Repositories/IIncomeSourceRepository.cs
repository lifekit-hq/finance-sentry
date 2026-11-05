namespace FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>The non-sensitive columns of an income source: readable on a host that holds no encryption key.</summary>
public sealed record IncomeSourceOutline(
    Guid Id, IncomeKind Kind, string Currency, IncomeCadence Cadence, IncomeBasis GrossOrNet, DateOnly StartDate, DateOnly? EndDate,
    DateTimeOffset? RetiredAt)
{
    public bool IsActiveOn(DateOnly day) => IncomeSource.IsActiveOn(StartDate, EndDate, RetiredAt, day);
}

public interface IIncomeSourceRepository
{
    /// <summary>Whether this host can read and write the encrypted fields (<c>Label</c>, <c>Amount</c>).</summary>
    bool CanReadEncryptedFields { get; }

    Task<IReadOnlyList<IncomeSource>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Never touches an encrypted column, so it works on a host without the key.</summary>
    Task<IReadOnlyList<IncomeSourceOutline>> ListOutlinesAsync(Guid userId, CancellationToken ct = default);

    Task<IncomeSource?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task AddAsync(IncomeSource source, CancellationToken ct = default);
    Task RemoveAsync(IncomeSource source, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

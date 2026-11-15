namespace FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>The non-sensitive columns of a tax profile: readable on a host that holds no encryption key.</summary>
public sealed record TaxProfileOutline(
    string? Jurisdiction, bool HasJurisdictionNotes, bool HasCaveat, DateTimeOffset UpdatedAt);

public interface ITaxProfileRepository
{
    /// <summary>Whether this host can read and write the encrypted fields (<c>JurisdictionNotes</c>, <c>Caveat</c>).</summary>
    bool CanReadEncryptedFields { get; }

    Task<TaxProfile?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Never materialises an encrypted column, so it works on a host without the key.</summary>
    Task<TaxProfileOutline?> GetOutlineAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(TaxProfile profile, CancellationToken ct = default);
    Task RemoveAsync(TaxProfile profile, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

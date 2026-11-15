namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// An in-memory tax-profile repository for handler tests. <see cref="CanReadEncryptedFields"/> is settable so a test can play
/// the MCP host, which holds no encryption key; <see cref="EncryptedColumnReads"/> counts reads of the free text so a test can
/// show the outline read never touches it.
/// </summary>
internal sealed class FakeTaxProfileRepository : ITaxProfileRepository
{
    public List<TaxProfile> Profiles { get; } = [];

    public bool CanReadEncryptedFields { get; set; } = true;

    public int EncryptedColumnReads { get; private set; }

    public int Saves { get; private set; }

    public Task<TaxProfile?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        EncryptedColumnReads++;
        return Task.FromResult(Profiles.FirstOrDefault(p => p.UserId == userId));
    }

    public Task<TaxProfileOutline?> GetOutlineAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(Profiles.Where(p => p.UserId == userId)
            .Select(p => new TaxProfileOutline(p.Jurisdiction, p.JurisdictionNotes is not null, p.Caveat is not null, p.UpdatedAt))
            .FirstOrDefault());

    public Task AddAsync(TaxProfile profile, CancellationToken ct = default)
    {
        Profiles.Add(profile);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(TaxProfile profile, CancellationToken ct = default)
    {
        Profiles.Remove(profile);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

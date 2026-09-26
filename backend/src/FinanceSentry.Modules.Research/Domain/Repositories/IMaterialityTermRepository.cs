namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface IMaterialityTermRepository
{
    /// <summary>The active keyword list <see cref="Infrastructure.Jobs.NewsMaterialityJob"/> matches against.</summary>
    Task<IReadOnlyList<string>> ListEnabledTermsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MaterialityTerm>> ListAllAsync(CancellationToken ct = default);

    Task<Guid> AddAsync(MaterialityTerm term, CancellationToken ct = default);

    Task UpdateAsync(MaterialityTerm term, CancellationToken ct = default);
}

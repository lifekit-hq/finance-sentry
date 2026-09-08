namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class NewsSourceRepository(ResearchDbContext db) : INewsSourceRepository
{
    public async Task<IReadOnlyList<NewsSource>> ListEnabledAsync(CancellationToken ct = default)
        => await db.NewsSources.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct);

    public async Task<IReadOnlyList<NewsSource>> ListDisabledAsync(CancellationToken ct = default)
        => await db.NewsSources.AsNoTracking().Where(s => !s.Enabled).ToListAsync(ct);

    public async Task<IReadOnlyList<NewsSource>> ListAllAsync(CancellationToken ct = default)
        => await db.NewsSources.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<NewsSource?> GetByUrlAsync(string url, CancellationToken ct = default)
        => await db.NewsSources.AsNoTracking().FirstOrDefaultAsync(s => s.Url == url, ct);

    public async Task<Guid> AddAsync(NewsSource source, CancellationToken ct = default)
    {
        db.NewsSources.Add(source);
        await db.SaveChangesAsync(ct);
        return source.Id;
    }

    public async Task UpdateAsync(NewsSource source, CancellationToken ct = default)
    {
        db.NewsSources.Update(source);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(NewsSource source, CancellationToken ct = default)
    {
        db.NewsSources.Remove(source);
        await db.SaveChangesAsync(ct);
    }
}

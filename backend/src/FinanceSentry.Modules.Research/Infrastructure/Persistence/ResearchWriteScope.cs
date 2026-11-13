namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

public sealed class ResearchWriteScope(ResearchDbContext db) : IResearchWriteScope
{
    public async Task<T> RunAsync<T>(ThesisEventOrigin origin, Func<Task<T>> work, CancellationToken ct = default)
    {
        if (origin != ThesisEventOrigin.Ledger || db.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }
}

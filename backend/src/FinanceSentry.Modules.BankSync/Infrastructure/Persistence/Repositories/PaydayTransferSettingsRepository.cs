namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>EF Core implementation of <see cref="IPaydayTransferSettingsRepository"/>; reads run under the Owner query filter.</summary>
public class PaydayTransferSettingsRepository(BankSyncDbContext context) : IPaydayTransferSettingsRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task<PaydayTransferSettings?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.PaydayTransferSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task<PaydayTransferSettings> UpsertAsync(
        Guid userId, decimal fixedOutflowUah, decimal roundingStepEur, CancellationToken cancellationToken = default)
    {
        var settings = await _context.PaydayTransferSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (settings is null)
        {
            settings = new PaydayTransferSettings { UserId = userId };
            _context.PaydayTransferSettings.Add(settings);
        }

        settings.FixedOutflowUah = fixedOutflowUah;
        settings.RoundingStepEur = roundingStepEur;
        await _context.SaveChangesAsync(cancellationToken);
        return settings;
    }
}

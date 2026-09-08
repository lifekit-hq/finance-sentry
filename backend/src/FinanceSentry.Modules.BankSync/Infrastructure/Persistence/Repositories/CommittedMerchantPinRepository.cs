namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="ICommittedMerchantPinRepository"/>.
/// </summary>
public class CommittedMerchantPinRepository(BankSyncDbContext context) : ICommittedMerchantPinRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommittedMerchantPin>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await _context.CommittedMerchantPins.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.MerchantKey)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetPinnedKeysAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var keys = await _context.CommittedMerchantPins.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.MerchantKey)
            .ToListAsync(cancellationToken);

        return keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task<CommittedMerchantPin?> FindAsync(
        Guid userId, string merchantKey, CancellationToken cancellationToken = default)
        => _context.CommittedMerchantPins.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MerchantKey == merchantKey, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(CommittedMerchantPin pin, CancellationToken cancellationToken = default)
    {
        _context.CommittedMerchantPins.Add(pin);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        Guid userId, string merchantKey, CancellationToken cancellationToken = default)
    {
        // Load-then-Remove rather than ExecuteDeleteAsync: the unique (UserId, MerchantKey)
        // index caps this at one row, and ExecuteDelete is unsupported by the in-memory
        // provider the module's own tests run on.
        var pin = await _context.CommittedMerchantPins
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MerchantKey == merchantKey, cancellationToken);
        if (pin is null)
            return false;

        _context.CommittedMerchantPins.Remove(pin);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

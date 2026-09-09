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
    public async Task<AddCommittedMerchantPinResult> AddIfAbsentAsync(
        CommittedMerchantPin pin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var existing = await FindAsync(pin.UserId, pin.MerchantKey, cancellationToken);
        if (existing is not null)
            return new AddCommittedMerchantPinResult(existing, AlreadyPinned: true);

        _context.CommittedMerchantPins.Add(pin);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new AddCommittedMerchantPinResult(pin, AlreadyPinned: false);
        }
        catch (DbUpdateException)
        {
            // Same shape as CompanionEventRepository.InsertIfNewAsync: the read above cannot be
            // atomic with the write, so a concurrent pin of the same merchant (two tabs, a
            // double-clicked button) loses the unique (UserId, MerchantKey) index here. That is
            // the state the caller asked for, so report it as already pinned rather than a 5xx.
            _context.Entry(pin).State = EntityState.Detached;

            var winner = await FindAsync(pin.UserId, pin.MerchantKey, cancellationToken);
            if (winner is null)
                throw; // Not a lost race — a real write failure the caller must hear about.

            return new AddCommittedMerchantPinResult(winner, AlreadyPinned: true);
        }
    }

    private Task<CommittedMerchantPin?> FindAsync(
        Guid userId, string merchantKey, CancellationToken cancellationToken)
        => _context.CommittedMerchantPins.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MerchantKey == merchantKey, cancellationToken);

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

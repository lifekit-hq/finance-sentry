namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="ICommittedMerchantPinRepository"/>.
/// </summary>
public class CommittedMerchantPinRepository(BankSyncDbContext context) : ICommittedMerchantPinRepository
{
    private const int MaxAddAttempts = 2;

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
        // Opts out of the Owner filter: the committed-outflow policy that reads this also runs inside the
        // family-clearing job with no person in scope; the explicit predicate keeps it scoped to the user.
        var keys = await _context.CommittedMerchantPins.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.MerchantKey)
            .ToListAsync(cancellationToken);

        return keys.ToHashSet(StringComparer.Ordinal);
    }

    // One INSERT ... ON CONFLICT DO NOTHING on the unique (UserId, MerchantKey) index: the write is atomic, so two
    // tabs (or a double-clicked button) pinning one merchant cannot fail on the index, and no read precedes it. The
    // affected-row count says which happened. It runs at once rather than at SaveChanges, and the pin is not
    // tracked by this context. A pin removed between the conflict and the read-back (a concurrent unpin) leaves
    // nothing to report, so the insert is tried once more.
    /// <inheritdoc />
    public async Task<AddCommittedMerchantPinResult> AddIfAbsentAsync(
        CommittedMerchantPin pin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pin);

        for (var attempt = 0; attempt < MaxAddAttempts; attempt++)
        {
            var inserted = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO bank_sync.committed_merchant_pins ("Id", "UserId", "MerchantKey", "DisplayName", "CreatedAt")
                VALUES ({pin.Id}, {pin.UserId}, {pin.MerchantKey}, {pin.DisplayName}, {pin.CreatedAt})
                ON CONFLICT ("UserId", "MerchantKey") DO NOTHING
                """,
                cancellationToken);
            if (inserted > 0)
                return new AddCommittedMerchantPinResult(pin, AlreadyPinned: false);

            var existing = await FindAsync(pin.UserId, pin.MerchantKey, cancellationToken);
            if (existing is not null)
                return new AddCommittedMerchantPinResult(existing, AlreadyPinned: true);
        }

        throw new InvalidOperationException(
            $"Pin for merchant '{pin.MerchantKey}' was neither inserted nor found after {MaxAddAttempts} attempts.");
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

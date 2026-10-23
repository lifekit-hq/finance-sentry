namespace FinanceSentry.Modules.BankSync.Infrastructure.Maintenance;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <inheritdoc />
public class RetiredPendingRepairService(
    BankSyncDbContext db,
    IProviderHistoryFetcher fetcher,
    ITransactionDeduplicationService dedup,
    ILogger<RetiredPendingRepairService> logger) : IRetiredPendingRepairService
{
    /// <summary>The first run of the reconciler (#320): nothing was retired by it before this moment.</summary>
    public static readonly DateTime RetiredSince = new(2026, 7, 23, 11, 20, 0, DateTimeKind.Utc);

    private readonly BankSyncDbContext _db = db;
    private readonly IProviderHistoryFetcher _fetcher = fetcher;
    private readonly ITransactionDeduplicationService _dedup = dedup;
    private readonly ILogger<RetiredPendingRepairService> _logger = logger;

    public async Task<RetiredPendingRepairResult> RepairAsync(bool dryRun, CancellationToken ct = default)
    {
        var before = await FindEligibleAsync(ct);
        _logger.LogInformation(
            "Retired-pending repair{Mode}: {Eligible} eligible row(s) on {Accounts} account(s) before.",
            dryRun ? " (dry run)" : "", before.Count, before.Select(t => t.AccountId).Distinct().Count());

        if (dryRun || before.Count == 0)
            return new RetiredPendingRepairResult(dryRun, before.Count, 0, 0, before.Count, 0);

        var accountIds = before.Select(t => t.AccountId).Distinct().ToList();
        var accounts = await _db.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(ct);

        var settled = 0;
        var unread = 0;
        foreach (var group in before.GroupBy(t => t.AccountId))
        {
            foreach (var row in group)
            {
                row.IsActive = true;
                row.DeletedAt = null;
            }

            var account = accounts.Single(a => a.Id == group.Key);
            IReadOnlyList<TransactionCandidate> candidates;
            try
            {
                candidates = await _fetcher.FetchAsync(account, group.Min(t => t.TransactionDate), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort per account: its rows stay restored and pending, and the next sync settles them.
                _logger.LogWarning(ex, "Retired-pending repair could not read account {AccountId} ({Provider}); its rows stay pending.",
                    account.Id, account.Provider);
                unread++;
                continue;
            }

            var booked = new Dictionary<string, TransactionCandidate>();
            foreach (var c in candidates.Where(c => !c.IsPending))
                booked[_dedup.ComputeHash(c.AccountId, c.Amount, c.HashDate, c.Description)] = c;

            foreach (var row in group.Where(r => booked.ContainsKey(r.UniqueHash)))
            {
                row.IsPending = false;
                row.PostedDate = booked[row.UniqueHash].PostedDate ?? row.PostedDate ?? row.TransactionDate;
                settled++;
            }
        }

        await _db.SaveChangesAsync(ct);

        var after = (await FindEligibleAsync(ct)).Count;
        _logger.LogInformation(
            "Retired-pending repair: reactivated {Reactivated}, settled {Settled}, {Unread} account(s) unread; eligible {Before} -> {After}.",
            before.Count, settled, unread, before.Count, after);

        return new RetiredPendingRepairResult(false, before.Count, before.Count, settled, after, unread);
    }

    // Tracked entities, so the repair edits them in place. Offline run: no person in scope, so it opts out of the
    // Owner filter; the soft-delete filter is opted out of to see the retired rows, and the account filter stays on.
    private async Task<List<Transaction>> FindEligibleAsync(CancellationToken ct)
    {
        var activeAccountIds = await _db.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(a => a.Id)
            .ToListAsync(ct);

        var retired = await _db.Transactions.IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.ActiveFilterName])
            .Where(t => !t.IsActive && t.IsPending && t.ArchivedReason == null
                        && t.DeletedAt >= RetiredSince && activeAccountIds.Contains(t.AccountId))
            .ToListAsync(ct);
        if (retired.Count == 0)
            return retired;

        var retiredAccountIds = retired.Select(t => t.AccountId).Distinct().ToList();
        var twins = (await _db.Transactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .AsNoTracking()
                .Where(t => retiredAccountIds.Contains(t.AccountId))
                .Select(t => new { t.AccountId, t.Amount, t.TransactionType, t.TransactionDate, t.PostedDate })
                .ToListAsync(ct))
            .Select(t => TwinKey(t.AccountId, t.Amount, t.TransactionType, t.PostedDate ?? t.TransactionDate))
            .ToHashSet();

        return retired
            .Where(t => !twins.Contains(TwinKey(t.AccountId, t.Amount, t.TransactionType, t.PostedDate ?? t.TransactionDate)))
            .ToList();
    }

    private static string TwinKey(Guid accountId, decimal amount, string? type, DateTime date) =>
        $"{accountId}|{amount:F2}|{type}|{date:yyyy-MM-dd}";
}

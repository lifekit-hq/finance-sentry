namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Monthly Hangfire job that enforces FR-008: 24-month transaction retention policy.
/// Soft-archives (does NOT hard-delete) transactions older than 24 months.
/// Idempotent: running twice for the same month archives the same set.
/// GDPR: archived rows stay in DB with IsActive=false, invisible to user-facing queries.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class DataRetentionJob(BankSyncDbContext db, ILogger<DataRetentionJob> logger)
{
    private readonly BankSyncDbContext _db = db;
    private readonly ILogger<DataRetentionJob> _logger = logger;

    private const int RetentionMonths = 24;
    private const string ArchiveReason = "retention_policy_24m";

    /// <summary>
    /// Runs the retention job.
    /// </summary>
    /// <param name="dryRun">
    /// When true, logs how many transactions would be archived without actually archiving.
    /// Use for ops validation before first production run.
    /// </param>
    public async Task RunAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-RetentionMonths);

        _logger.LogInformation(
            "DataRetentionJob starting. Cutoff date: {Cutoff}. DryRun: {DryRun}",
            cutoff.ToString("yyyy-MM-dd"), dryRun);

        // Sweeps every user (no person in scope) and includes already-inactive rows (idempotency), so it
        // opts out of both the Owner and the soft-delete filter by name.
        var candidates = _db.Transactions
            .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.ActiveFilterName])
            .Where(t => t.IsActive
                     && t.PostedDate.HasValue
                     && t.PostedDate.Value < cutoff);

        if (dryRun)
        {
            _logger.LogInformation(
                "DataRetentionJob found {Count} transactions to archive (posted before {Cutoff}).",
                await candidates.CountAsync(ct), cutoff.ToString("yyyy-MM-dd"));
            _logger.LogInformation("DataRetentionJob dry-run complete. No changes written.");
            return;
        }

        // One set-based UPDATE: the archive never loads the rows into memory.
        var now = DateTime.UtcNow;
        var archived = await candidates.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(t => t.IsActive, false)
                .SetProperty(t => t.DeletedAt, now)
                .SetProperty(t => t.ArchivedReason, ArchiveReason),
            ct);

        _logger.LogInformation(
            "DataRetentionJob completed. Archived {Count} transactions (posted before {Cutoff}). Timestamp: {Timestamp}",
            archived, cutoff.ToString("yyyy-MM-dd"), now);
    }
}

namespace FinanceSentry.Modules.Analytics.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Analytics.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Writable context for the <c>analytics</c> schema (feature 033). Owns only the <c>query_audit</c>
/// table; the curated views + <c>fs_readonly</c> role + RLS are created via raw SQL in migration M001
/// and are not modelled here (they are read through a separate read-only connection).
/// </summary>
public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string Schema = "analytics";

    public DbSet<QueryAuditRecord> QueryAudit => Set<QueryAuditRecord>();

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<QueryAuditRecord>(e =>
        {
            e.ToTable("query_audit");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
            e.Property(x => x.Sql).HasMaxLength(8000);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RejectReason).HasMaxLength(500);
        });
    }
}

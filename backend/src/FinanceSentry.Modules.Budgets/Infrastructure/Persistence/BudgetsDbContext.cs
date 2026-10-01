namespace FinanceSentry.Modules.Budgets.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Budgets.Domain;
using Microsoft.EntityFrameworkCore;

public class BudgetsDbContext(DbContextOptions<BudgetsDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<Budget> Budgets { get; set; } = null!;

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("budgets");
        base.OnModelCreating(modelBuilder);

        var bb = modelBuilder.Entity<Budget>();
        bb.ToTable("budgets");
        bb.HasKey(b => b.Id);
        bb.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
        bb.Property(b => b.UserId).IsRequired();
        bb.Property(b => b.Category).IsRequired().HasMaxLength(50);
        bb.Property(b => b.MonthlyLimit).HasPrecision(15, 2).IsRequired();
        bb.Property(b => b.Currency).IsRequired().HasMaxLength(3);
        bb.Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        bb.Property(b => b.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        bb.HasIndex(b => b.UserId)
            .HasDatabaseName("idx_budget_user_id");

        bb.HasIndex(b => new { b.UserId, b.Category })
            .IsUnique()
            .HasDatabaseName("idx_budget_user_category_unique");

        bb.HasQueryFilter(OwnerQueryFilter.Name, b => b.UserId == CurrentUserId);
    }
}

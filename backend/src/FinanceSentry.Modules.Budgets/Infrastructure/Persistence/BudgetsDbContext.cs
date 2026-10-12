namespace FinanceSentry.Modules.Budgets.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Modules.Budgets.Domain;
using Microsoft.EntityFrameworkCore;

public class BudgetsDbContext(DbContextOptions<BudgetsDbContext> options, ICurrentUser currentUser)
    : OwnerScopedDbContext(options, currentUser)
{
    private const int MoneyPrecision = 15;
    private const int MoneyScale = 2;

    public DbSet<Budget> Budgets { get; set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.HaveGeneratedGuidKeyDefault().HaveDecimalPrecision(MoneyPrecision, MoneyScale);
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("budgets");

        var bb = modelBuilder.Entity<Budget>();
        bb.ToTable("budgets");
        bb.HasKey(b => b.Id);
        bb.Property(b => b.UserId).IsRequired();
        bb.Property(b => b.Category).IsRequired().HasMaxLength(50);
        bb.Property(b => b.MonthlyLimit).IsRequired();
        bb.Property(b => b.Currency).IsRequired().HasMaxLength(3);
        bb.Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        bb.Property(b => b.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        bb.HasIndex(b => b.UserId)
            .HasDatabaseName("idx_budget_user_id");

        bb.HasIndex(b => new { b.UserId, b.Category })
            .IsUnique()
            .HasDatabaseName("idx_budget_user_category_unique");
    }
}

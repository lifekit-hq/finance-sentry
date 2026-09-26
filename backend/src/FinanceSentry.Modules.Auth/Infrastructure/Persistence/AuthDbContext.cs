using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

public class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<McpAuthorizationCode> McpAuthorizationCodes => Set<McpAuthorizationCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<McpServiceToken> McpServiceTokens => Set<McpServiceToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("auth");
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.UserId).IsRequired();
        });

        builder.Entity<McpAuthorizationCode>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.CodeHash).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.UserId).IsRequired();
            entity.Property(t => t.Email).IsRequired();
            entity.Property(t => t.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.RedirectUri).IsRequired();
        });

        builder.Entity<McpServiceToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.UserId).IsRequired();
            entity.Property(t => t.Label).HasMaxLength(200).IsRequired();
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.SafeWithdrawalRate).HasDefaultValue(0.04m);
            entity.Property(u => u.RealAnnualReturn).HasDefaultValue(0.05m);
        });
    }
}

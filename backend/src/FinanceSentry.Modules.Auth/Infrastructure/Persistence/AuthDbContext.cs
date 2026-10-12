using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

// The Owner query filter covers the per-user credential rows (RefreshToken, McpAuthorizationCode). Identity's own
// tables (users, roles, user claims/roles/logins/tokens) stay unfiltered: sign-in, token refresh, invite acceptance,
// the per-request principal load and the role seed read them before a person is in scope, and managing people reads
// them across users. McpServiceToken is not filtered yet: its revocation check runs while the MCP host is still
// authenticating, before a person is in scope. DataProtectionKeys is the platform's key ring, shared by every
// instance and read by the key manager with no person in scope, so it is unfiltered too.
public class AuthDbContext(DbContextOptions<AuthDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<McpAuthorizationCode> McpAuthorizationCodes => Set<McpAuthorizationCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<McpServiceToken> McpServiceTokens => Set<McpServiceToken>();

    // Read by the Owner query filter on every query this context runs. Identity user ids are strings holding
    // Guid.ToString() (lower-case "D" format), so the acting person's id is compared in that same form; null (no
    // person in scope) matches no row.
    private string? CurrentUserKey => currentUser.UserId?.ToString();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("auth");
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasQueryFilter(OwnerQueryFilter.Name, t => t.UserId == CurrentUserKey);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.UserId).IsRequired();
        });

        builder.Entity<McpAuthorizationCode>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasQueryFilter(OwnerQueryFilter.Name, t => t.UserId == CurrentUserKey);
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

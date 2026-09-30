namespace FinanceSentry.Tests.Integration.Shared;

using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Seeds the local account a test access token names. The API builds the request principal from that
/// account, so a token whose <c>sub</c> has no account is rejected with 401.
/// </summary>
public static class TestUsers
{
    private static readonly Lock SeedLock = new();

    public static void EnsureExists(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var id = userId.ToString();
        var email = $"{id}@test.local";

        lock (SeedLock)
        {
            if (db.Users.Any(u => u.Id == id))
                return;

            db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            db.SaveChanges();
        }
    }
}

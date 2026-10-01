namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Builds a throwaway in-memory <see cref="ResearchDbContext"/> for companion-layer tests.</summary>
internal static class CompanionTestContext
{
    /// <remarks>Acts as <paramref name="actingUser"/>; null (the default) is no person in scope, as in a job.</remarks>
    public static ResearchDbContext Create(Guid? actingUser = null)
    {
        var options = new DbContextOptionsBuilder<ResearchDbContext>()
            .UseInMemoryDatabase($"companion-{Guid.NewGuid():N}")
            .Options;
        return new ResearchDbContext(options, new FixedCurrentUser(actingUser));
    }
}

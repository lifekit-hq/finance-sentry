using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Risk.Tests;

internal static class TestSupport
{
    // The in-memory provider applies the Owner query filter too, so the context acts for the person under test.
    public static RiskDbContext NewContext(Guid actingUser)
    {
        var options = new DbContextOptionsBuilder<RiskDbContext>()
            .UseInMemoryDatabase($"risk-tests-{Guid.NewGuid():N}")
            .Options;
        return new RiskDbContext(options, new FixedCurrentUser(actingUser));
    }
}

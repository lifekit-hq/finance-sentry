namespace FinanceSentry.Tests.Unit.Persistence;

using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The registration a context holding profile data uses: <c>AddAuditedWrites()</c> once, <c>UseAuditedWrites(sp)</c> in its
/// <c>AddDbContext</c> factory. With those two lines a save reaches the trail as the host's actor - no handler code.
/// </summary>
public sealed class AuditedWriteWiringTests
{
    [Fact]
    public async Task AContextWiredWithTheTwoCalls_PutsItsWritesOnTheTrail_AsTheHostsActor()
    {
        var person = Guid.NewGuid();
        var trail = new RecordingTrail();
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);
        services.AddScoped<IAuditActor>(_ => new AuditActor(new FixedUser(person), () => AuditActorKind.McpPersonal));
        services.AddAuditedWrites();
        services.AddDbContext<WiredDbContext>((sp, options) => options.UseInMemoryDatabase(databaseName).UseAuditedWrites(sp));
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WiredDbContext>();
            db.Goals.Add(new GoalRow { UserId = person, Title = "never recorded" });
            await db.SaveChangesAsync();
        }

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.ResourceType.Should().Be("goal");
        entry.ActorKind.Should().Be(AuditActorKind.McpPersonal);
        entry.FieldNames.Should().Equal("Title");
    }

    private sealed class FixedUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default)
        {
            Entries.AddRange(entries);
            return Task.CompletedTask;
        }
    }

    [AuditedProfileData("goal")]
    private sealed class GoalRow : IUserOwned
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private sealed class WiredDbContext(DbContextOptions<WiredDbContext> options) : DbContext(options)
    {
        public DbSet<GoalRow> Goals => Set<GoalRow>();
    }
}

namespace FinanceSentry.Tests.Unit.Persistence;

using FinanceSentry.Core.Domain;
using FinanceSentry.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class UpdatedAtSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 30, 0, TimeSpan.Zero);

    private readonly string _databaseName = Guid.NewGuid().ToString();

    private StampDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(new UpdatedAtSaveChangesInterceptor(new FixedClock(Now)))
            .Options;
        return new StampDbContext(options);
    }

    private async Task<Guid> SeedAsync()
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.Offsets.Add(new OffsetRow { Id = id, Name = "a", UpdatedAt = Seeded });
        db.Instants.Add(new InstantRow { Id = id, Name = "a", UpdatedAt = Seeded.UtcDateTime });
        db.NullableInstants.Add(new NullableInstantRow { Id = id, Name = "a" });
        db.Plain.Add(new PlainRow { Id = id, Name = "a", UpdatedAt = Seeded });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_StampsEachClrTypeFromTimeProvider()
    {
        var id = await SeedAsync();

        await using (var db = CreateContext())
        {
            (await db.Offsets.SingleAsync(x => x.Id == id)).Name = "b";
            (await db.Instants.SingleAsync(x => x.Id == id)).Name = "b";
            (await db.NullableInstants.SingleAsync(x => x.Id == id)).Name = "b";
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Offsets.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Now);
        (await verify.Instants.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Now.UtcDateTime);
        (await verify.NullableInstants.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_StampsOnTheSynchronousPath()
    {
        var id = await SeedAsync();

        await using (var db = CreateContext())
        {
            db.Offsets.Single(x => x.Id == id).Name = "b";
            db.SaveChanges();
        }

        await using var verify = CreateContext();
        (await verify.Offsets.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task SaveChangesAsync_UnmodifiedEntity_IsNotStamped()
    {
        var id = await SeedAsync();

        await using (var db = CreateContext())
        {
            _ = await db.Offsets.SingleAsync(x => x.Id == id);
            var touched = await db.Instants.SingleAsync(x => x.Id == id);
            touched.Name = touched.Name; // assignment of the same value is not a change
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Offsets.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Seeded);
        (await verify.Instants.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Seeded.UtcDateTime);
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_KeepsItsOwnValue()
    {
        var id = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.Offsets.Add(new OffsetRow { Id = id, Name = "new", UpdatedAt = Seeded });
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Offsets.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Seeded);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntityWithoutMarker_IsNotStamped()
    {
        var id = await SeedAsync();

        await using (var db = CreateContext())
        {
            (await db.Plain.SingleAsync(x => x.Id == id)).Name = "b";
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Plain.SingleAsync(x => x.Id == id)).UpdatedAt.Should().Be(Seeded);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OffsetRow : IHasUpdatedAt
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class InstantRow : IHasUpdatedAt
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class NullableInstantRow : IHasUpdatedAt
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; private set; }
    }

    private sealed class PlainRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class StampDbContext(DbContextOptions<StampDbContext> options) : DbContext(options)
    {
        public DbSet<OffsetRow> Offsets => Set<OffsetRow>();
        public DbSet<InstantRow> Instants => Set<InstantRow>();
        public DbSet<NullableInstantRow> NullableInstants => Set<NullableInstantRow>();
        public DbSet<PlainRow> Plain => Set<PlainRow>();
    }
}

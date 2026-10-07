namespace FinanceSentry.Tests.Integration.Radar;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="RadarDbContext"/>: global signals (no UserId) stay visible to every person,
/// a holder-scoped signal only to its holder, and a context with no person in scope sees only the global ones. The
/// writer's dedup check, the retention prune and the weekly brief's read run with no person in scope, so they opt out
/// explicitly and neither miss a holder's signals nor re-insert them. Real Postgres is required, matching the other
/// filter suites (the prune is an ExecuteDelete the in-memory provider cannot run).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RadarOwnerQueryFilterTests : IAsyncLifetime
{
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private RadarDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<RadarDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static RadarSignal NewSignal(
        Guid? userId,
        SignalSeverity severity = SignalSeverity.Notable,
        DateTimeOffset? timestamp = null,
        string? dedupKey = null) => new()
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Scanner = RadarScanners.Portfolio,
            SignalType = RadarSignalTypes.AllocationDrift,
            Severity = severity,
            SubjectType = RadarSubjectTypes.AssetClass,
            Subject = "Equity",
            UserId = userId,
            DedupKey = dedupKey ?? Guid.NewGuid().ToString("N"),
            Payload = new Dictionary<string, object> { ["driftPct"] = 10m },
        };

    private async Task SeedAsync(params RadarSignal[] signals)
    {
        // Inserts are not filtered, so a no-person context writes any holder's rows.
        await using var seed = CreateContext();
        seed.RadarSignals.AddRange(signals);
        await seed.SaveChangesAsync();
    }

    private async Task<List<RadarSignal>> AllSignalsAsync()
    {
        await using var read = CreateContext();
        return await read.RadarSignals.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().ToListAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().ContainSingle().Which.ClrType.Should().Be<RadarSignal>();
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_global_signals_and_only_their_own_holder_signals()
    {
        var global = NewSignal(userId: null);
        var a = NewSignal(_userA);
        var b = NewSignal(_userB);
        await SeedAsync(global, a, b);

        await using (var asA = CreateContext(_userA))
            (await asA.RadarSignals.Select(x => x.Id).ToListAsync()).Should().BeEquivalentTo([global.Id, a.Id]);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.RadarSignals.Select(x => x.Id).ToListAsync()).Should().BeEquivalentTo([global.Id, b.Id]);
            (await asB.RadarSignals.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.RadarSignals.Select(x => x.Id).ToListAsync()).Should().Equal(
            [global.Id], "no person in scope still sees the global signals, and no holder's");
    }

    [DockerRequiredFact]
    public async Task Repository_list_without_a_user_filter_returns_only_what_the_acting_person_may_see()
    {
        var global = NewSignal(userId: null);
        var a = NewSignal(_userA);
        await SeedAsync(global, a, NewSignal(_userB));

        await using var asA = CreateContext(_userA);
        var listed = await new RadarSignalRepository(asA).ListAsync(new SignalFilter());

        listed.Select(s => s.Id).Should().BeEquivalentTo([global.Id, a.Id]);
    }

    [DockerRequiredFact]
    public async Task Writer_with_no_person_dedups_a_holder_signal_instead_of_inserting_it_again()
    {
        var request = new RadarSignalRequest(
            RadarScanners.Portfolio, RadarSignalTypes.AllocationDrift, SignalSeverity.Notable,
            RadarSubjectTypes.AssetClass, "Equity", _userA, $"drift:{_userA:N}",
            new Dictionary<string, object> { ["driftPct"] = 10m });

        await using (var ctx = CreateContext())
        {
            var writer = new RadarSignalWriter(new RadarSignalRepository(ctx), Options.Create(new RadarOptions()));
            (await writer.AppendSignalAsync(request)).Should().BeTrue();
            (await writer.AppendSignalAsync(request)).Should().BeFalse(
                "a filtered dedup check would miss the holder's first signal and append a duplicate");
        }

        (await AllSignalsAsync()).Should().ContainSingle(s => s.UserId == _userA);
    }

    [DockerRequiredFact]
    public async Task Prune_with_no_person_removes_every_holders_old_info_signals_and_keeps_notable_ones()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        var keptNotable = NewSignal(_userA, SignalSeverity.Notable, old);
        var recentInfo = NewSignal(_userB, SignalSeverity.Info);
        await SeedAsync(
            NewSignal(userId: null, SignalSeverity.Info, old),
            NewSignal(_userA, SignalSeverity.Info, old),
            NewSignal(_userB, SignalSeverity.Info, old),
            keptNotable,
            recentInfo);

        await using (var ctx = CreateContext())
            (await new RadarSignalRepository(ctx).PruneInfoBeforeUnscopedAsync(DateTimeOffset.UtcNow.AddDays(-30)))
                .Should().Be(3);

        (await AllSignalsAsync()).Select(s => s.Id).Should().BeEquivalentTo([keptNotable.Id, recentInfo.Id]);
    }

    [DockerRequiredFact]
    public async Task Brief_read_with_no_person_returns_global_and_that_holders_signals_only()
    {
        var global = NewSignal(userId: null);
        var a = NewSignal(_userA);
        await SeedAsync(global, a, NewSignal(_userB));

        await using var ctx = CreateContext();
        var listed = await new RadarSignalRepository(ctx).ListForUserUnscopedAsync(
            _userA, new SignalFilter(Scanner: RadarScanners.Portfolio, UserId: _userB));

        listed.Select(s => s.Id).Should().BeEquivalentTo(
            [global.Id, a.Id], "the given user replaces any user in the filter");
    }
}

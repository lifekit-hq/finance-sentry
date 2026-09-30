namespace FinanceSentry.Tests.Integration.CrossModulePorts;

using FinanceSentry.Integration;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// 039 (US2/SC-002): the allocation port translates the IPS (single home of target allocation) into
/// the fraction target + symmetric drift band the Risk drift comparator consumes. A band that was
/// migrated from the Risk side (encoded as min/max = target ± band) must round-trip exactly.
/// #673: composed over Research's published <c>IIpsAllocationReader</c> port.
/// </summary>
public sealed class IpsAllocationPolicySourceTests
{
    private sealed class StubGetIps(IpsDto? dto) : IQueryHandler<GetIpsQuery, IpsDto?>
    {
        public Task<IpsDto?> Handle(GetIpsQuery query, CancellationToken cancellationToken) => Task.FromResult(dto);
    }

    private static IpsDto IpsWith(params AllocationTarget[] targets) => new(
        Guid.NewGuid(), 1, true, [], 10, null, 3, null, null, null,
        targets, RebalancingRule.Default, null, null, 90, [], "annual", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static IpsAllocationPolicySource Source(IpsDto? dto) => new(new IpsAllocationReader(new StubGetIps(dto)));

    [Fact]
    public async Task Translates_MigratedMinMaxBand_ToExactFractionTargetAndBand()
    {
        // Risk-origin allocation migrated as min/max = (target ± band)·100: Equity 0.60 target, 0.05 band.
        var source = Source(IpsWith(new AllocationTarget("Equity", 60m, 55m, 65m)));

        var result = await source.GetAllocationTargetsAsync(Guid.NewGuid(), CancellationToken.None);

        var t = result.Should().ContainSingle().Subject;
        t.AssetClass.Should().Be("Equity");
        t.TargetPct.Should().Be(0.60m);
        t.DriftBandPct.Should().Be(0.05m);
    }

    [Fact]
    public async Task Translates_BandReachingZeroLowerBound_WithoutFallingBackToRule()
    {
        // Edge: target 0.05, band 0.05 → min 0, max 10. A set max (10>0) is authoritative even though
        // the lower bound is 0; must NOT fall back to the RebalancingRule.
        var source = Source(IpsWith(new AllocationTarget("Cash", 5m, 0m, 10m)));

        var result = await source.GetAllocationTargetsAsync(Guid.NewGuid(), CancellationToken.None);

        var t = result.Should().ContainSingle().Subject;
        t.TargetPct.Should().Be(0.05m);
        t.DriftBandPct.Should().Be(0.05m);
    }

    [Fact]
    public async Task DerivesBandFromRebalancingRule_WhenNoExplicitMinMax()
    {
        // IPS-native allocation with no min/max → derive band as GetAllocationDriftQuery does:
        // max(abs 5, target·rel/100) then /100. Target 60% → max(5, 60·25/100=15)=15 → 0.15 fraction.
        var source = Source(IpsWith(new AllocationTarget("Equity", 60m, 0m, 0m)));

        var result = await source.GetAllocationTargetsAsync(Guid.NewGuid(), CancellationToken.None);

        var t = result.Should().ContainSingle().Subject;
        t.TargetPct.Should().Be(0.60m);
        t.DriftBandPct.Should().Be(0.15m);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoIps()
    {
        var source = Source(null);

        var result = await source.GetAllocationTargetsAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsEmpty_WhenIpsHasNoTargets()
    {
        var source = Source(IpsWith());

        var result = await source.GetAllocationTargetsAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}

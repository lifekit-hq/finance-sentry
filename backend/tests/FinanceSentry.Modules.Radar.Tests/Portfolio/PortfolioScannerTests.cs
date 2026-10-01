using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceSentry.Modules.Radar.Tests.Portfolio;

public sealed class PortfolioScannerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-0000-0000-0000-000000000001");

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeScanDataReader(
        IReadOnlyList<Guid> userIds,
        PortfolioScanData? data) : IPortfolioScanDataReader
    {
        public Task<IReadOnlyList<Guid>> GetScanUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult(userIds);

        public Task<PortfolioScanData?> ReadAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(data);
    }

    private static PortfolioScanData MakeData(
        decimal totalUsd = 100_000m,
        decimal cashUsd = 5_000m,
        bool isStale = false,
        IReadOnlyList<string>? staleSources = null,
        IReadOnlyList<ScanSleeveDrift>? driftRows = null,
        IReadOnlyList<ScanPosition>? positions = null,
        decimal? maxPositionWeightPct = null,
        decimal? minCashBufferPct = null) =>
        new(
            totalUsd,
            cashUsd,
            isStale,
            staleSources ?? [],
            driftRows ?? [],
            positions ?? [],
            maxPositionWeightPct,
            minCashBufferPct);

    private static ComputePortfolioSignalsCommandHandler Handler(RadarDbContext db, PortfolioScanData? data)
    {
        var repo = new RadarSignalRepository(db);
        var writer = new RadarSignalWriter(repo, TestSupport.Options());
        var reader = new FakeScanDataReader([UserId], data);
        return new ComputePortfolioSignalsCommandHandler(
            reader, writer, NullLogger<ComputePortfolioSignalsCommandHandler>.Instance);
    }

    private static async Task<IReadOnlyList<RadarSignal>> AllSignalsAsync(RadarDbContext db)
        => await new RadarSignalRepository(db).ListForUserUnscopedAsync(
            UserId, new SignalFilter(Scanner: RadarScanners.Portfolio));

    /// <summary>Payload numbers round-trip through jsonb as <see cref="JsonElement"/>.</summary>
    private static decimal PayloadDecimal(RadarSignal signal, string key)
        => signal.Payload[key] switch
        {
            JsonElement el => el.GetDecimal(),
            decimal d => d,
            _ => throw new InvalidOperationException($"Payload key '{key}' is not numeric."),
        };

    // ── empty book ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyBook_EmitsNoSignals()
    {
        await using var db = TestSupport.NewContext();
        var summary = await Handler(db, null).Handle(new ComputePortfolioSignalsCommand(), default);

        (await AllSignalsAsync(db)).Should().BeEmpty();
        summary.SignalsEmitted.Should().Be(0);
    }

    [Fact]
    public async Task ZeroTotalUsd_EmitsNoSignals()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(totalUsd: 0m, cashUsd: 0m);
        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        (await AllSignalsAsync(db)).Should().BeEmpty();
    }

    // ── allocation drift ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("OverBand", SignalSeverity.Notable)]
    [InlineData("UnderBand", SignalSeverity.Notable)]
    [InlineData("Within", SignalSeverity.Info)]
    [InlineData("Unplanned", SignalSeverity.Info)]
    public async Task AllocationDrift_SeverityMappedByStatus(string status, SignalSeverity expected)
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(driftRows:
        [
            new ScanSleeveDrift("Equity", 60m, 70m, 10m, status),
        ]);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signals = await AllSignalsAsync(db);
        var driftSignal = signals.Single(s => s.SignalType == RadarSignalTypes.AllocationDrift);
        driftSignal.Severity.Should().Be(expected);
        driftSignal.Subject.Should().Be("Equity");
        driftSignal.SubjectType.Should().Be(RadarSubjectTypes.AssetClass);
        driftSignal.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task NoIps_EmitsNoDriftSignals()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(driftRows: []);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signals = await AllSignalsAsync(db);
        signals.Should().NotContain(s => s.SignalType == RadarSignalTypes.AllocationDrift);
    }

    [Fact]
    public async Task MultiSleeve_EmitsOneSignalPerSleeve()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(driftRows:
        [
            new ScanSleeveDrift("Equity", 60m, 70m, 10m, "OverBand"),
            new ScanSleeveDrift("Cash", 5m, 3m, -2m, "UnderBand"),
            new ScanSleeveDrift("Crypto", 10m, 10m, 0m, "Within"),
        ]);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var driftSignals = (await AllSignalsAsync(db))
            .Where(s => s.SignalType == RadarSignalTypes.AllocationDrift)
            .ToList();
        driftSignals.Should().HaveCount(3);
        driftSignals.Select(s => s.Subject).Should().BeEquivalentTo(["Equity", "Cash", "Crypto"]);
    }

    // ── concentration weight ─────────────────────────────────────────────────

    [Fact]
    public async Task TopPosition_Notable_WhenOverLimit()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(
            totalUsd: 100_000m,
            positions: [new ScanPosition("NVDA", 30_000m, 30m)],
            maxPositionWeightPct: 25m);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.ConcentrationWeight);
        signal.Severity.Should().Be(SignalSeverity.Notable);
        signal.Subject.Should().Be("NVDA");

        // The cap reaches the payload — and the brief's action line — as percentage points, the
        // same unit as weightPct. PortfolioScanDataReader converts the stored fraction; if it ever
        // passes 0.25 through again, a 30% position beats the "cap" by two orders of magnitude.
        PayloadDecimal(signal, "limitPct").Should().Be(25m);
        PayloadDecimal(signal, "weightPct").Should().Be(30m);
    }

    [Fact]
    public async Task TopPosition_Info_WhenWithinLimit()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(
            totalUsd: 100_000m,
            positions: [new ScanPosition("NVDA", 15_000m, 15m)],
            maxPositionWeightPct: 25m);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.ConcentrationWeight);
        signal.Severity.Should().Be(SignalSeverity.Info);
    }

    [Fact]
    public async Task NoPositions_EmitsNoConcentrationSignal()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(positions: [], maxPositionWeightPct: 25m);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        (await AllSignalsAsync(db)).Should()
            .NotContain(s => s.SignalType == RadarSignalTypes.ConcentrationWeight);
    }

    // ── cash buffer ──────────────────────────────────────────────────────────
    // minCashBufferPct arrives as percentage points (5 = a 5% floor), converted from the fraction
    // the risk rule set stores by PortfolioScanDataReader. CashPct is on the same scale.

    [Fact]
    public async Task CashBuffer_Notable_WhenBelowMinimum()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(totalUsd: 100_000m, cashUsd: 2_000m, minCashBufferPct: 5m);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.CashBuffer);
        signal.Severity.Should().Be(SignalSeverity.Notable);
        PayloadDecimal(signal, "cashPct").Should().Be(2m);
        PayloadDecimal(signal, "minCashBufferPct").Should().Be(5m);
    }

    [Fact]
    public async Task CashBuffer_Info_WhenAtOrAboveMinimum()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(totalUsd: 100_000m, cashUsd: 7_000m, minCashBufferPct: 5m);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.CashBuffer);
        signal.Severity.Should().Be(SignalSeverity.Info);
    }

    [Fact]
    public async Task CashBuffer_Info_WhenExactlyAtTheFloor()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(totalUsd: 100_000m, cashUsd: 5_000m, minCashBufferPct: 5m);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.CashBuffer);
        signal.Severity.Should().Be(SignalSeverity.Info);
    }

    [Fact]
    public async Task NoCashBufferRule_EmitsNoCashBufferSignal()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(minCashBufferPct: null);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        (await AllSignalsAsync(db)).Should()
            .NotContain(s => s.SignalType == RadarSignalTypes.CashBuffer);
    }

    // ── sync health ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncHealth_Notable_WhenBookIsStale()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(isStale: true, staleSources: ["BankSync", "BrokerageSync"]);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.SyncHealth);
        signal.Severity.Should().Be(SignalSeverity.Notable);
    }

    [Fact]
    public async Task SyncHealth_Info_WhenBookIsFresh()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(isStale: false, staleSources: []);

        await Handler(db, data).Handle(new ComputePortfolioSignalsCommand(), default);

        var signal = (await AllSignalsAsync(db))
            .Single(s => s.SignalType == RadarSignalTypes.SyncHealth);
        signal.Severity.Should().Be(SignalSeverity.Info);
    }

    // ── idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SameDay_SecondRun_SuppressesAllSignals()
    {
        await using var db = TestSupport.NewContext();
        var data = MakeData(
            totalUsd: 100_000m,
            cashUsd: 2_000m,
            isStale: true,
            staleSources: ["BankSync"],
            driftRows: [new ScanSleeveDrift("Equity", 60m, 70m, 10m, "OverBand")],
            positions: [new ScanPosition("NVDA", 30_000m, 30m)],
            maxPositionWeightPct: 25m,
            minCashBufferPct: 5m);

        var handler = Handler(db, data);
        var first = await handler.Handle(new ComputePortfolioSignalsCommand(), default);
        var second = await handler.Handle(new ComputePortfolioSignalsCommand(), default);

        first.SignalsEmitted.Should().BeGreaterThan(0);
        second.SignalsEmitted.Should().Be(0);
        second.SignalsSuppressed.Should().Be(first.SignalsEmitted);

        // Total signal count after two runs equals signal count after one run.
        var afterTwo = await AllSignalsAsync(db);
        afterTwo.Should().HaveCount(first.SignalsEmitted);
    }
}

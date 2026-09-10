using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.Radar.Tests;

internal static class TestSupport
{
    public static RadarDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<RadarDbContext>()
            .UseInMemoryDatabase($"radar-tests-{Guid.NewGuid():N}")
            .Options;
        return new RadarDbContext(options);
    }

    public static IOptions<RadarOptions> Options(RadarOptions? options = null)
        => Microsoft.Extensions.Options.Options.Create(options ?? new RadarOptions());
}

/// <summary>Fake history source: seeded bars per ticker; throws for a designated failing ticker.</summary>
internal sealed class FakeHistorySource(
    IReadOnlyDictionary<string, IReadOnlyList<DailyBarData>> barsByTicker,
    string? throwForTicker = null) : IMarketHistorySource
{
    /// <summary>Tickers an upstream fetch was attempted for, in order — the run's rate-limit cost.</summary>
    public List<string> Requested { get; } = [];

    public Task<IReadOnlyList<DailyBarData>> GetDailyBarsAsync(
        string ticker, DateOnly since, CancellationToken ct = default)
    {
        this.Requested.Add(ticker);

        if (throwForTicker is not null && ticker == throwForTicker)
        {
            throw new InvalidOperationException($"Source failed for {ticker}");
        }

        // Return the full seeded series regardless of `since`; UpsertRange enforces idempotency.
        var bars = barsByTicker.TryGetValue(ticker, out var b) ? b : [];
        return Task.FromResult(bars);
    }
}

/// <summary>Fake universe service returning a fixed active member set (no external readers).</summary>
internal sealed class FakeUniverseService(IReadOnlyList<RadarUniverseMember> members) : IRadarUniverseService
{
    public Task<IReadOnlyList<RadarUniverseMember>> SyncAsync(CancellationToken ct = default)
        => Task.FromResult(members);

    public Task<IReadOnlyList<RadarUniverseMember>> GetActiveAsync(CancellationToken ct = default)
        => Task.FromResult(members);
}

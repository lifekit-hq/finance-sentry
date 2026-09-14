using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.Radar.Tests;

internal static class TestSupport
{
    /// <summary>
    /// A fresh in-memory Radar database. Note the provider cannot translate
    /// <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> — a repository method written that way
    /// (e.g. <c>RadarUniverseRepository.DeactivateAsync</c>) throws here rather than running, so
    /// prove those paths against a mocked repository or a Postgres-backed test instead.
    /// </summary>
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

/// <summary>Fake stage-1 shortlist: a settable ticker list plus the count of reads it served.</summary>
internal sealed class FakeShortlistSource : IScanShortlistSource
{
    public IReadOnlyList<string> Tickers { get; set; } = [];

    /// <summary>Times the universe asked stage 1 for a shortlist — the funnel's upstream cost per run.</summary>
    public int Reads { get; private set; }

    public Task<IReadOnlyList<string>> GetShortlistAsync(CancellationToken ct = default)
    {
        this.Reads++;
        return Task.FromResult(this.Tickers);
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

/// <summary>Counts bar reads per ticker so a per-member fan-out cannot creep back in unnoticed.</summary>
internal sealed class CountingDailyBarRepository(IDailyBarRepository inner) : IDailyBarRepository
{
    public Dictionary<string, int> ReadsByTicker { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> UpsertRangeAsync(IReadOnlyCollection<DailyBar> bars, CancellationToken ct = default)
        => inner.UpsertRangeAsync(bars, ct);

    public Task<IReadOnlyList<DailyBar>> GetSinceAsync(string ticker, DateOnly since, CancellationToken ct = default)
    {
        this.ReadsByTicker[ticker] = this.ReadsByTicker.TryGetValue(ticker, out var count) ? count + 1 : 1;
        return inner.GetSinceAsync(ticker, since, ct);
    }

    public Task<DateOnly?> GetLatestDateAsync(string ticker, CancellationToken ct = default)
        => inner.GetLatestDateAsync(ticker, ct);

    public Task<IReadOnlyDictionary<string, DateOnly>> GetLatestDatesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => inner.GetLatestDatesAsync(tickers, ct);
}

internal sealed class CountingUniverseRepository(IRadarUniverseRepository inner) : IRadarUniverseRepository
{
    public int ActiveListings { get; set; }

    public Task<IReadOnlyList<RadarUniverseMember>> ListActiveAsync(CancellationToken ct = default)
    {
        this.ActiveListings++;
        return inner.ListActiveAsync(ct);
    }

    public Task<IReadOnlyList<RadarUniverseMember>> ListAllAsync(CancellationToken ct = default)
        => inner.ListAllAsync(ct);

    public Task UpsertMembersAsync(IReadOnlyCollection<RadarUniverseMember> members, CancellationToken ct = default)
        => inner.UpsertMembersAsync(members, ct);

    public Task DeactivateAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => inner.DeactivateAsync(tickers, ct);
}

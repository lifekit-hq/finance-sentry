namespace FinanceSentry.Modules.Radar.Tests;

using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Radar's real upserts are single <c>INSERT ... ON CONFLICT</c> statements, which the in-memory provider cannot
/// run. Tests that only need a store to seed and read back use these doubles (tracked read-then-write, reads
/// delegated to the real repository); the real upsert is proven on Postgres in the integration project.
/// </summary>
internal sealed class InMemoryDailyBarRepository(RadarDbContext db) : IDailyBarRepository
{
    private readonly DailyBarRepository reads = new(db);

    public async Task<int> UpsertRangeAsync(IReadOnlyCollection<DailyBar> bars, CancellationToken ct = default)
    {
        var existing = await db.DailyBars.ToDictionaryAsync(b => (b.Ticker, b.Date), ct);
        var added = 0;
        foreach (var bar in bars.GroupBy(b => (b.Ticker, b.Date)).Select(g => g.First()))
        {
            if (existing.TryGetValue((bar.Ticker, bar.Date), out var row))
            {
                row.Open = bar.Open;
                row.High = bar.High;
                row.Low = bar.Low;
                row.Close = bar.Close;
                row.AdjClose = bar.AdjClose;
                row.Volume = bar.Volume;
            }
            else
            {
                db.DailyBars.Add(bar);
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    public Task<IReadOnlyList<DailyBar>> GetSinceAsync(string ticker, DateOnly since, CancellationToken ct = default)
        => reads.GetSinceAsync(ticker, since, ct);

    public Task<DateOnly?> GetLatestDateAsync(string ticker, CancellationToken ct = default)
        => reads.GetLatestDateAsync(ticker, ct);

    public Task<IReadOnlyDictionary<string, DateOnly>> GetLatestDatesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => reads.GetLatestDatesAsync(tickers, ct);
}

internal sealed class InMemoryRadarUniverseRepository(RadarDbContext db) : IRadarUniverseRepository
{
    private readonly RadarUniverseRepository reads = new(db);

    public async Task UpsertMembersAsync(IReadOnlyCollection<RadarUniverseMember> members, CancellationToken ct = default)
    {
        var existing = await db.UniverseMembers.ToDictionaryAsync(m => m.Ticker, ct);
        foreach (var member in members)
        {
            if (existing.TryGetValue(member.Ticker, out var current))
            {
                current.Active = true;
                current.Kind = member.Kind;
                current.Source = member.Source;
            }
            else
            {
                db.UniverseMembers.Add(member);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<RadarUniverseMember>> ListActiveAsync(CancellationToken ct = default)
        => reads.ListActiveAsync(ct);

    public Task<IReadOnlyList<RadarUniverseMember>> ListAllAsync(CancellationToken ct = default)
        => reads.ListAllAsync(ct);

    public Task DeactivateAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => reads.DeactivateAsync(tickers, ct);
}

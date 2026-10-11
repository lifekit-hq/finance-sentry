namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Infrastructure.Caching;
using FluentAssertions;
using Xunit;

public sealed class TtlCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    [Fact]
    public async Task Hit_WithinTtl_DoesNotFetchAgain_AndExpiredEntryFetchesOnce()
    {
        var clock = new ManualTimeProvider(Start);
        using var sut = new TtlCache<int>(clock, Ttl, maxEntries: 10);
        var calls = 0;
        Task<(int, bool)> Fetch(CancellationToken _) => Task.FromResult((++calls, true));

        await sut.GetOrFetchAsync("AAPL", Fetch, CancellationToken.None);
        clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        (await sut.GetOrFetchAsync("aapl", Fetch, CancellationToken.None)).Should().Be(1);
        calls.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(1));
        (await sut.GetOrFetchAsync("AAPL", Fetch, CancellationToken.None)).Should().Be(2);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task UncacheableResult_IsNotStored()
    {
        using var sut = new TtlCache<int>(new ManualTimeProvider(Start), Ttl, maxEntries: 10);
        var calls = 0;

        await sut.GetOrFetchAsync("AAPL", _ => Task.FromResult((++calls, false)), CancellationToken.None);
        await sut.GetOrFetchAsync("AAPL", _ => Task.FromResult((++calls, false)), CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentMissesOnOneKey_ShareOneFetch()
    {
        using var sut = new TtlCache<int>(new ManualTimeProvider(Start), Ttl, maxEntries: 10);
        var calls = 0;
        var release = new TaskCompletionSource();

        async Task<(int, bool)> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return (7, true);
        }

        var pending = Enumerable.Range(0, 8)
            .Select(_ => sut.GetOrFetchAsync("AAPL", Fetch, CancellationToken.None))
            .ToArray();
        release.SetResult();

        (await Task.WhenAll(pending)).Should().OnlyContain(v => v == 7);
        calls.Should().Be(1);
    }

    [Fact]
    public void SizeLimit_EvictsInsteadOfGrowingPastMaxEntries()
    {
        using var sut = new TtlCache<int>(new ManualTimeProvider(Start), Ttl, maxEntries: 2);

        for (var i = 0; i < 20; i++)
        {
            sut.Set($"T{i}", i);
        }

        Enumerable.Range(0, 20).Count(i => sut.TryGetFresh($"T{i}", out _)).Should().BeLessThanOrEqualTo(2);
    }
}

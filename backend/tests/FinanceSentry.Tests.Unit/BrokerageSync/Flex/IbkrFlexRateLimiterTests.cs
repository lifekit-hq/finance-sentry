using System.Diagnostics;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

public class IbkrFlexRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_EnforcesMinimumIntervalBetweenCalls()
    {
        var limiter = new IbkrFlexRateLimiter(maxPerWindow: 100, window: TimeSpan.FromMinutes(1), minInterval: TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        await limiter.WaitAsync();
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(190));
    }

    [Fact]
    public async Task WaitAsync_EnforcesMaxRequestsPerWindow()
    {
        var limiter = new IbkrFlexRateLimiter(maxPerWindow: 2, window: TimeSpan.FromMilliseconds(200), minInterval: TimeSpan.Zero);
        var stopwatch = Stopwatch.StartNew();

        await limiter.WaitAsync();
        await limiter.WaitAsync();
        // The window only holds 2 requests; the 3rd must wait for the first to expire.
        await limiter.WaitAsync();

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180));
    }

    [Fact]
    public async Task WaitAsync_UnderTheLimit_DoesNotDelay()
    {
        var limiter = new IbkrFlexRateLimiter(maxPerWindow: 100, window: TimeSpan.FromMinutes(1), minInterval: TimeSpan.Zero);
        var stopwatch = Stopwatch.StartNew();

        await limiter.WaitAsync();
        await limiter.WaitAsync();

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }
}

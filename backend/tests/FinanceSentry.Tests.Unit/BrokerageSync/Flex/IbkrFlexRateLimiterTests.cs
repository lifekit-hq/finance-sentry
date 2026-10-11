using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

public class IbkrFlexRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_AllowsMaxRequestsPerWindow_ThenHoldsTheNext()
    {
        const int maxPerWindow = 3;
        using var limiter = new IbkrFlexRateLimiter(maxPerWindow, window: TimeSpan.FromMinutes(1), minInterval: TimeSpan.Zero);

        for (var i = 0; i < maxPerWindow; i++)
            limiter.WaitAsync().IsCompletedSuccessfully.Should().BeTrue($"call {i + 1} fits in the window");

        using var cts = new CancellationTokenSource();
        var overflow = limiter.WaitAsync(cts.Token);
        overflow.IsCompleted.Should().BeFalse("the window holds only 3 requests until the first expires");

        await cts.CancelAsync();
        await overflow.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitAsync_HoldsASecondCallInsideTheMinimumInterval()
    {
        using var limiter = new IbkrFlexRateLimiter(maxPerWindow: 100, window: TimeSpan.FromMinutes(1), minInterval: TimeSpan.FromSeconds(1));

        limiter.WaitAsync().IsCompletedSuccessfully.Should().BeTrue();

        using var cts = new CancellationTokenSource();
        var second = limiter.WaitAsync(cts.Token);
        second.IsCompleted.Should().BeFalse("IBKR allows one request per second");

        await cts.CancelAsync();
        await second.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void WaitAsync_UnderTheLimit_DoesNotDelay()
    {
        using var limiter = new IbkrFlexRateLimiter(maxPerWindow: 100, window: TimeSpan.FromMinutes(1), minInterval: TimeSpan.Zero);

        limiter.WaitAsync().IsCompletedSuccessfully.Should().BeTrue();
        limiter.WaitAsync().IsCompletedSuccessfully.Should().BeTrue();
    }
}

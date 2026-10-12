namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FluentAssertions;
using Xunit;

public sealed class MonobankBalanceCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entry_IsFreshUntilNinetySeconds_ThenExpires()
    {
        var clock = new ManualClock(Start);
        using var sut = new MonobankBalanceCache(clock);
        sut.Set("token", "acc-1", Info("acc-1"));

        clock.Advance(TimeSpan.FromSeconds(89));
        sut.TryGet("token", "acc-1").Should().NotBeNull();
        sut.HasFreshEntries("token").Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1));
        sut.TryGet("token", "acc-1").Should().BeNull();
        sut.HasFreshEntries("token").Should().BeFalse();
    }

    [Fact]
    public void HasFreshEntries_IsPerToken()
    {
        using var sut = new MonobankBalanceCache(new ManualClock(Start));
        sut.Set("token-a", "acc-1", Info("acc-1"));

        sut.HasFreshEntries("token-a").Should().BeTrue();
        sut.HasFreshEntries("token-b").Should().BeFalse();
    }

    [Fact]
    public void Set_Again_RefreshesTheEntry()
    {
        var clock = new ManualClock(Start);
        using var sut = new MonobankBalanceCache(clock);
        sut.Set("token", "acc-1", Info("acc-1"));
        clock.Advance(TimeSpan.FromSeconds(60));
        sut.Set("token", "acc-1", Info("acc-1"));

        clock.Advance(TimeSpan.FromSeconds(60));

        sut.TryGet("token", "acc-1").Should().NotBeNull();
    }

    private static BankAccountInfo Info(string id) => new(id, "Jar", "black", "1234", 10m, "UAH", "Owner");

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

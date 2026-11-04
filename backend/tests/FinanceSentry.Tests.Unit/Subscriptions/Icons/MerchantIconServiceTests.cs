namespace FinanceSentry.Tests.Unit.Subscriptions.Icons;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

public class MerchantIconServiceTests
{
    private const string Host = "www.example.com";
    private static readonly FetchedIcon Png = new("image/png", [1, 2, 3]);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IMerchantIconFetcher> _fetcher = new();
    private readonly SubscriptionsDbContext _db = new(
        new DbContextOptionsBuilder<SubscriptionsDbContext>().UseInMemoryDatabase($"icons-{Guid.NewGuid():N}").Options,
        NoCurrentUser.Instance);

    private readonly MerchantIconService _service;

    public MerchantIconServiceTests()
    {
        var catalog = new Mock<IMerchantBrandCatalog>();
        catalog.Setup(c => c.Find(7)).Returns(new MerchantBrand { Id = 7, Keyword = "example", Domain = Host });
        _service = new MerchantIconService(_db, catalog.Object, _fetcher.Object, _clock);
    }

    [Fact]
    public async Task Get_FirstRequest_FetchesOnceAndCaches()
    {
        _fetcher.Setup(f => f.FetchAsync(Host, It.IsAny<CancellationToken>())).ReturnsAsync(Png);

        var first = await _service.GetAsync(7, default);
        var second = await _service.GetAsync(7, default);

        first.Should().BeEquivalentTo(new MerchantIconContent("image/png", [1, 2, 3]));
        second.Should().BeEquivalentTo(first);
        _fetcher.Verify(f => f.FetchAsync(Host, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_PositiveEntryExpires_FetchesAgain()
    {
        _fetcher.Setup(f => f.FetchAsync(Host, It.IsAny<CancellationToken>())).ReturnsAsync(Png);
        await _service.GetAsync(7, default);

        _clock.Advance(MerchantIconService.PositiveTtl - TimeSpan.FromMinutes(1));
        await _service.GetAsync(7, default);
        _fetcher.Verify(f => f.FetchAsync(Host, It.IsAny<CancellationToken>()), Times.Once);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.GetAsync(7, default);
        _fetcher.Verify(f => f.FetchAsync(Host, It.IsAny<CancellationToken>()), Times.Exactly(2));
        (await _db.MerchantIcons.CountAsync()).Should().Be(1, "the expired entry is refreshed, not duplicated");
    }

    [Fact]
    public async Task Get_FetchFindsNothing_CachesTheMissAndDoesNotAskAgainUntilItExpires()
    {
        _fetcher.Setup(f => f.FetchAsync(Host, It.IsAny<CancellationToken>())).ReturnsAsync((FetchedIcon?)null);

        (await _service.GetAsync(7, default)).Should().BeNull();
        (await _service.GetAsync(7, default)).Should().BeNull();
        _fetcher.Verify(f => f.FetchAsync(Host, It.IsAny<CancellationToken>()), Times.Once);

        var entry = await _db.MerchantIcons.SingleAsync();
        entry.IsNegative.Should().BeTrue();
        entry.ExpiresAt.Should().Be(_clock.GetUtcNow() + MerchantIconService.NegativeTtl);

        _fetcher.Setup(f => f.FetchAsync(Host, It.IsAny<CancellationToken>())).ReturnsAsync(Png);
        _clock.Advance(MerchantIconService.NegativeTtl + TimeSpan.FromMinutes(1));
        (await _service.GetAsync(7, default)).Should().NotBeNull("an outage heals once the negative entry expires");
    }

    [Fact]
    public async Task Get_UnknownBrand_IsRefusedWithoutAnyFetch()
    {
        (await _service.GetAsync(999, default)).Should().BeNull();

        _fetcher.Verify(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KnownMissing_ReturnsOnlyUnexpiredNegativeHosts()
    {
        var now = _clock.GetUtcNow();
        _db.MerchantIcons.AddRange(
            new MerchantIcon { Domain = "missing.example", ExpiresAt = now.AddHours(1), FetchedAt = now },
            new MerchantIcon { Domain = "expired.example", ExpiresAt = now.AddHours(-1), FetchedAt = now },
            new MerchantIcon { Domain = "found.example", Content = [1], ContentType = "image/png", ExpiresAt = now.AddDays(1), FetchedAt = now });
        await _db.SaveChangesAsync();

        var missing = await _service.GetKnownMissingAsync(
            ["missing.example", "expired.example", "found.example", "unseen.example"], default);

        missing.Should().BeEquivalentTo(["missing.example"]);
    }
}

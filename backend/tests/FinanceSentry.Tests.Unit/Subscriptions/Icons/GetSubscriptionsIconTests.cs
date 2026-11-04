namespace FinanceSentry.Tests.Unit.Subscriptions.Icons;

using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetSubscriptionsIconTests
{
    private static readonly MerchantBrand Netflix = new() { Id = 3, Keyword = "netflix", Domain = "www.netflix.com" };

    private static DetectedSubscription Row(string display) =>
        DetectedSubscription.Create(
            "user-1", display.ToLowerInvariant(), display, "monthly", 10m, 10m, "USD",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), 3, 90, null);

    private static async Task<IReadOnlyList<int?>> IconIdsAsync(IReadOnlyCollection<string> missingHosts, params string[] names)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>();
        repo.Setup(r => r.GetByUserIdAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names.Select(Row).ToList());
        repo.Setup(r => r.GetActiveByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(names.Select(Row).ToList());
        var catalog = new Mock<IMerchantBrandCatalog>();
        catalog.Setup(c => c.Match(It.IsAny<string?>()))
            .Returns((string? n) => n is not null && n.Contains("Netflix", StringComparison.OrdinalIgnoreCase) ? Netflix : null);
        var icons = new Mock<IMerchantIconService>();
        icons.Setup(i => i.GetKnownMissingAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingHosts.ToHashSet());

        var result = await new GetSubscriptionsQueryHandler(repo.Object, catalog.Object, icons.Object)
            .Handle(new GetSubscriptionsQuery("user-1", false), default);
        return result.Items.Select(i => i.IconId).ToList();
    }

    [Fact]
    public async Task Handle_MatchedMerchant_CarriesTheBrandIdAndUnmatchedCarriesNone() =>
        (await IconIdsAsync([], "Netflix", "Local Gym")).Should().Equal(3, null);

    [Fact]
    public async Task Handle_HostKnownToHaveNoIcon_CarriesNoBrandId() =>
        (await IconIdsAsync(["www.netflix.com"], "Netflix")).Should().Equal((int?)null);
}

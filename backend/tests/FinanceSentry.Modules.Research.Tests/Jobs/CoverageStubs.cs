namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Moq;

/// <summary>The coverage collaborators a detector test does not care about: an empty universe and a reviewer that does nothing.</summary>
internal static class CoverageStubs
{
    public static ICoverageRepository NoMembers() => WithMembers();

    /// <summary>A coverage store holding exactly these members (all added a long time ago, never evented).</summary>
    public static ICoverageRepository WithMembers(params string[] tickers)
    {
        var repository = new Mock<ICoverageRepository>();
        repository.Setup(r => r.ListMembersUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. tickers.Select(t => new CoverageMember { Ticker = t, AddedReason = CoverageReason.Screen })]);
        return repository.Object;
    }

    public static ICoverageEventReviewer NoReviewer() => Mock.Of<ICoverageEventReviewer>();
}

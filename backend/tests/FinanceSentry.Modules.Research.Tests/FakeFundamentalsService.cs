namespace FinanceSentry.Modules.Research.Tests;

using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// Fundamentals-chain double serving fixed facts for every ticker: <c>covered</c> when there are any,
/// <c>no_source_available</c> otherwise. For the chain's own behaviour see <c>FundamentalsChainServiceTests</c>.
/// </summary>
internal sealed class FakeFundamentalsService(IReadOnlyList<FundamentalFact>? facts = null) : IFundamentalsService
{
    public Task<FundamentalsResult> GetFundamentalsAsync(string ticker, int maxPerConcept, CancellationToken ct = default)
        => Task.FromResult(ResultFor(ticker, facts ?? []));

    public static FundamentalsResult ResultFor(string ticker, IReadOnlyList<FundamentalFact> facts)
        => new(
            ticker,
            facts,
            facts.Count > 0
                ? new FundamentalsCoverage(FundamentalsCoverageStatus.Covered, null, null, [])
                : new FundamentalsCoverage(FundamentalsCoverageStatus.NoSourceAvailable, null, "no facts seeded", []));
}

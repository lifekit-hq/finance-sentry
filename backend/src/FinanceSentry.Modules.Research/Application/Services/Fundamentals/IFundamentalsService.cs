namespace FinanceSentry.Modules.Research.Application.Services.Fundamentals;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// The one fundamentals read every consumer uses (<c>get_fundamentals</c>, <c>score_candidate</c>,
/// <c>run_thesis_monitor</c>, the scan's grading, valuation history): the provider chain's normalized
/// facts plus an explicit coverage answer. Readers never see which provider ran.
/// </summary>
public interface IFundamentalsService
{
    /// <summary>
    /// Revenue, GrossProfit, OperatingIncome, NetIncome, DilutedEPS and StockholdersEquity for the
    /// ticker, up to <paramref name="maxPerConcept"/> newest datapoints per concept (clamped 1-20),
    /// ordered by concept then newest period first. Every fact carries its <see cref="SourceProvenance"/>.
    /// </summary>
    Task<FundamentalsResult> GetFundamentalsAsync(string ticker, int maxPerConcept, CancellationToken ct = default);
}

/// <summary>The chain's answer: the facts (empty only with a non-covered <see cref="Coverage"/>) and why.</summary>
public sealed record FundamentalsResult(
    string Ticker,
    IReadOnlyList<FundamentalFact> Facts,
    FundamentalsCoverage Coverage);

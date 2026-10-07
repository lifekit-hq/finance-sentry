namespace FinanceSentry.Modules.Research.Application.Services.Fundamentals;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// One fundamentals provider in the ordered chain (#837). A source maps whatever it reads into the
/// normalized <see cref="FundamentalFact"/> shape and stamps every fact with its
/// <see cref="SourceProvenance"/>; it never trims, merges or ranks — that is the chain's job. Adding a
/// provider is one implementation plus one entry in <c>Fundamentals:SourceOrder</c>; no reader changes.
/// </summary>
public interface IFundamentalsSource
{
    /// <summary>Stable provider name: written into provenance and matched by <c>Fundamentals:SourceOrder</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Every fact the provider has for the ticker, all periods of both bases. A provider error is
    /// reported as <see cref="FundamentalsSourceOutcome.Failed"/>, never as an empty answer, so the
    /// chain can tell "this provider has nothing" from "this provider is down".
    /// </summary>
    Task<FundamentalsSourceResult> FetchAsync(string ticker, CancellationToken ct);
}

public enum FundamentalsSourceOutcome
{
    /// <summary>The provider answered with at least one fact.</summary>
    Facts,

    /// <summary>The provider answered and has nothing for this ticker.</summary>
    NoData,

    /// <summary>The provider could not be read (network, rate limit, malformed payload).</summary>
    Failed,
}

/// <summary>
/// A source's answer. <see cref="IssuerType"/> is set by a source that can tell (EDGAR, from what the
/// issuer files), <see cref="Detail"/> explains a <see cref="FundamentalsSourceOutcome.NoData"/> or
/// <see cref="FundamentalsSourceOutcome.Failed"/> answer.
/// </summary>
public sealed record FundamentalsSourceResult(
    FundamentalsSourceOutcome Outcome,
    IReadOnlyList<FundamentalFact> Facts,
    string? IssuerType = null,
    string? Detail = null)
{
    public static FundamentalsSourceResult WithFacts(IReadOnlyList<FundamentalFact> facts, string? issuerType = null)
        => new(FundamentalsSourceOutcome.Facts, facts, issuerType);

    public static FundamentalsSourceResult NoData(string detail, string? issuerType = null)
        => new(FundamentalsSourceOutcome.NoData, [], issuerType, detail);

    public static FundamentalsSourceResult Failed(string detail, string? issuerType = null)
        => new(FundamentalsSourceOutcome.Failed, [], issuerType, detail);
}

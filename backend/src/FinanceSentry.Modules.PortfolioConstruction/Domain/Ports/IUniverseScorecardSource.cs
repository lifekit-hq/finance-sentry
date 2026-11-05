namespace FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;

/// <summary>
/// Consumer-side port: the scorecards of a set of symbols, produced by the unchanged 019 scorers behind the
/// Research module's scorecard read (spec 726 FR-002). Shared corpus: nothing in a scorecard depends on the
/// viewer. Implemented in FinanceSentry.Integration so this module never references Research.
/// </summary>
public interface IUniverseScorecardSource
{
    /// <summary>
    /// One entry per requested symbol, in the order asked. A symbol whose scorecard cannot be read comes back with
    /// a <see cref="UniverseScorecard.FailureReason"/> and no facts, so one bad name never blanks the rest.
    /// </summary>
    Task<IReadOnlyList<UniverseScorecard>> GetAsync(IReadOnlyList<string> symbols, CancellationToken ct);
}

/// <summary>
/// Consumer-side port: the viewer's own constraints on a construction (spec 726 FR-014, FR-015): the IPS's
/// exclusions and sleeve targets, and the risk rule set's limits. Implemented in FinanceSentry.Integration
/// over Research's IPS read and Risk's published limits port, both unchanged.
/// </summary>
public interface IConstructionPolicySource
{
    Task<ConstructionPolicy> GetAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Consumer-side port: the equity names the viewer holds (the universe adds them, spec 726 OQ-1). Implemented in
/// FinanceSentry.Integration over Core's brokerage holdings read.
/// </summary>
public interface IHeldEquitySource
{
    Task<IReadOnlyList<string>> ListSymbolsAsync(Guid userId, CancellationToken ct);
}

namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;

/// <summary>
/// One provider in a measured sleeve. <paramref name="LinkedOn"/> is the UTC day its earliest active connection was
/// created (a relink re-stamps it). <paramref name="FlowsRecorded"/> says whether the provider's external flows are
/// known (a broker: it has ever reported a deposit or withdrawal): until they are, no figure may read its value change
/// as growth.
/// </summary>
public sealed record FlowSourceCoverage(string Source, DateOnly LinkedOn, bool FlowsRecorded);

/// <summary>
/// One provider's classified cash movement, converted to USD at its own date's rate (<paramref name="Fx"/>).
/// <paramref name="UnmatchedTransfer"/> marks a transfer leg read as external only because no leg on another
/// provider in the sleeve matched it (<c>UNMATCHED_TRANSFER</c>): it may be a move inside the sleeve.
/// </summary>
public sealed record SourceFlow(string Source, ClassifiedFlow Flow, FxStatus Fx, bool UnmatchedTransfer = false);

/// <summary>
/// Value moving between two providers of the sleeve, held by neither: sent by <paramref name="Source"/> at the close
/// of <paramref name="From"/> and every day after, up to the day it arrived (<paramref name="Until"/>, excluded; null
/// while it has not), valued in USD (<paramref name="Fx"/>) at what arrives or is expected to.
/// </summary>
public sealed record InTransitValue(string Source, DateOnly From, DateOnly? Until, decimal ValueUsd, FxStatus Fx)
{
    /// <summary>Whether the value is in transit at the close of <paramref name="date"/>.</summary>
    public bool HeldOn(DateOnly date) => From <= date && (Until is not { } until || date < until);
}

/// <summary>
/// A sleeve's providers and their classified flows dated after each provider's link day, and the value in transit
/// between them (<see cref="InTransit"/>), which no provider's own value holds.
/// </summary>
public sealed record SleeveFlowReading(IReadOnlyList<FlowSourceCoverage> Sources, IReadOnlyList<SourceFlow> Flows)
{
    public static SleeveFlowReading Empty { get; } = new([], []);

    /// <summary>Value sent by one provider of the sleeve and not yet received by another.</summary>
    public IReadOnlyList<InTransitValue> InTransit { get; init; } = [];

    /// <summary>The USD value in transit at the close of <paramref name="date"/>: part of the sleeve's value that day.</summary>
    public decimal InTransitOn(DateOnly date) => InTransit.Where(t => t.HeldOn(date)).Sum(t => t.ValueUsd);

    /// <summary>The providers with a transfer leg read as external for want of a match, dated after <paramref name="opening"/> up to <paramref name="closing"/>.</summary>
    public IReadOnlyList<string> UnmatchedTransferSources(DateOnly opening, DateOnly closing)
        => [.. Flows.Where(f => f.UnmatchedTransfer && f.Flow.Date > opening && f.Flow.Date <= closing)
            .Select(f => f.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The providers linked by <paramref name="closing"/> that have never reported a deposit or withdrawal.</summary>
    public IReadOnlyList<string> SourcesWithoutFlows(DateOnly closing)
        => [.. Sources.Where(s => s.LinkedOn <= closing && !s.FlowsRecorded).Select(s => s.Source).Order(StringComparer.Ordinal)];

    /// <summary>The flows' classified movements, for the measure engine.</summary>
    public IReadOnlyList<ClassifiedFlow> ClassifiedFlows => [.. Flows.Select(f => f.Flow)];

    /// <summary>
    /// The worst conversion status among the flows dated after <paramref name="opening"/> up to
    /// <paramref name="closing"/> and the value in transit at either close.
    /// </summary>
    public FxStatus FxWithin(DateOnly opening, DateOnly closing)
        => Flows.Where(f => f.Flow.Date > opening && f.Flow.Date <= closing)
            .Select(f => f.Fx)
            .Concat(InTransit.Where(t => t.HeldOn(opening) || t.HeldOn(closing)).Select(t => t.Fx))
            .Aggregate(FxStatus.Exact, (worst, fx) => worst.Worst(fx));
}

/// <summary>
/// Reads the brokerage sleeve's external flows (deposits, withdrawals) and return flows (income, fees, taxes)
/// per provider from the broker-reported cash transactions (finance-framework §3.2), classified for the
/// brokerage scope and converted to USD at each flow's date. Runs with no person in scope (a job, the MCP
/// host), so implementations opt out of the Owner query filter and keep their own user predicate.
/// </summary>
public interface IBrokerageFlowReader
{
    /// <summary>
    /// Every provider linked to <paramref name="userId"/>'s brokerage sleeve and its flows dated after
    /// <paramref name="from"/> (and after its own link day: earlier rows are history the linked value
    /// already holds).
    /// </summary>
    Task<SleeveFlowReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}

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

/// <summary>One provider's classified cash movement, converted to USD at its own date's rate (<paramref name="Fx"/>).</summary>
public sealed record SourceFlow(string Source, ClassifiedFlow Flow, FxStatus Fx);

/// <summary>A sleeve's providers and their classified flows dated after each provider's link day.</summary>
public sealed record SleeveFlowReading(IReadOnlyList<FlowSourceCoverage> Sources, IReadOnlyList<SourceFlow> Flows)
{
    public static SleeveFlowReading Empty { get; } = new([], []);

    /// <summary>The providers linked by <paramref name="closing"/> that have never reported a deposit or withdrawal.</summary>
    public IReadOnlyList<string> SourcesWithoutFlows(DateOnly closing)
        => [.. Sources.Where(s => s.LinkedOn <= closing && !s.FlowsRecorded).Select(s => s.Source).Order(StringComparer.Ordinal)];

    /// <summary>The flows' classified movements, for the measure engine.</summary>
    public IReadOnlyList<ClassifiedFlow> ClassifiedFlows => [.. Flows.Select(f => f.Flow)];

    /// <summary>The worst conversion status among the flows dated after <paramref name="opening"/> up to <paramref name="closing"/>.</summary>
    public FxStatus FxWithin(DateOnly opening, DateOnly closing)
        => Flows.Where(f => f.Flow.Date > opening && f.Flow.Date <= closing)
            .Aggregate(FxStatus.Exact, (worst, f) => worst.Worst(f.Fx));
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

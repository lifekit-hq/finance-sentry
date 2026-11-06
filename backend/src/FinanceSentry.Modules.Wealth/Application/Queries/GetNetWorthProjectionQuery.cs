namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;

/// <summary>
/// Whether there is a twelve-month projection: below three complete months of flow (or with no snapshot) there
/// is none, and the dashboard renders nothing rather than a guess. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NetWorthProjectionStatus>))]
public enum NetWorthProjectionStatus
{
    Projected,
    InsufficientHistory,
}

/// <summary>
/// <summary>
/// The savings path with the person's career events applied, shown next to the baseline and never replacing it (#454 S5).
/// <paramref name="ProjectedNetWorth"/> is the same range as the baseline's, on the baseline's horizon; it is null when the
/// events could not be read on this host. <paramref name="Label"/> and <paramref name="Basis"/> are the words it is shown with.
/// </summary>
public record NetWorthScenario(
    ScenarioStatus Status,
    int EventCount,
    ProjectionRange? ProjectedNetWorth,
    string Label = ScenarioText.Label,
    string Basis = ScenarioText.Basis);

/// <summary>
/// The net-worth projection on the dashboard hero (M10, C8): the latest snapshot's net worth plus the savings
/// path, compounded at the shared return assumption for <paramref name="HorizonMonths"/>, as a range in today's
/// money. Every input is returned so the line can state its own assumptions in words. The handler computes in
/// USD; the API controller re-expresses the amounts in the profile base currency.
/// </summary>
public record NetWorthProjectionResponse(
    NetWorthProjectionStatus Status,
    int HorizonMonths,
    decimal CurrentNetWorth,
    decimal MonthlySavings,
    int CompleteMonths,
    ProjectionRange? ProjectedNetWorth,
    ProjectionRange RealAnnualReturnRange,
    bool HasStaleSleeves,
    string BaseCurrency = "USD",
    NetWorthScenario? Scenario = null);

public record GetNetWorthProjectionQuery(Guid UserId) : IQuery<NetWorthProjectionResponse>;

public class GetNetWorthProjectionQueryHandler(ProjectionInputsReader inputs, ScenarioReader scenarios)
    : IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse>
{
    /// <summary>How far forward the hero's projection looks. Fixed: there is no goal entity yet.</summary>
    public const int HorizonMonths = 12;

    private readonly ProjectionInputsReader _inputs = inputs;
    private readonly ScenarioReader _scenarios = scenarios;

    public async Task<NetWorthProjectionResponse> Handle(GetNetWorthProjectionQuery query, CancellationToken cancellationToken)
    {
        var inputs = await _inputs.ReadAsync(query.UserId, cancellationToken);
        var returns = inputs.Assumptions.RealAnnualReturns;

        if (inputs.Snapshot is null || inputs.SavingsPath is null)
        {
            return new NetWorthProjectionResponse(
                NetWorthProjectionStatus.InsufficientHistory,
                HorizonMonths,
                CurrentNetWorth: inputs.Snapshot?.TotalNetWorth ?? 0m,
                MonthlySavings: 0m,
                inputs.CompleteMonths.Count,
                ProjectedNetWorth: null,
                returns,
                inputs.HasStaleSleeves);
        }

        var current = inputs.Snapshot.TotalNetWorth;
        var savings = inputs.SavingsPath.MonthlySavingsUsd;
        var scenario = await _scenarios.ReadAsync(query.UserId, cancellationToken);

        return new NetWorthProjectionResponse(
            NetWorthProjectionStatus.Projected,
            HorizonMonths,
            current,
            savings,
            inputs.SavingsPath.CompleteMonths,
            Projection.ValueAfter(HorizonMonths, current, savings, inputs.Assumptions),
            returns,
            inputs.HasStaleSleeves,
            Scenario: scenario.Status == ScenarioStatus.None
                ? null
                : new NetWorthScenario(
                    scenario.Status,
                    scenario.Events.Count,
                    scenario.IsApplied
                        ? Projection.ValueAfter(HorizonMonths, current, savings, inputs.Assumptions, scenario.Deltas(HorizonMonths))
                        : null));
    }
}

namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;

/// <summary>
/// Whether there is a monthly-income figure: none before the first snapshot or while net worth is zero or
/// negative, and the dashboard then renders nothing. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MonthlyIncomeLensStatus>))]
public enum MonthlyIncomeLensStatus
{
    Shown,
    NoNetWorth,
}

/// <summary>
/// The monthly-income lens on the dashboard hero (P5): the latest snapshot's net worth drawn at the shared safe
/// withdrawal rate (C8), as a monthly amount in today's money. The net worth and the rate are returned so the
/// line can state its own assumptions in words. The handler computes in USD; the API controller re-expresses
/// the amounts in the profile base currency.
/// </summary>
public record MonthlyIncomeLensResponse(
    MonthlyIncomeLensStatus Status,
    decimal NetWorth,
    decimal SafeWithdrawalRate,
    decimal? MonthlyIncome,
    bool HasStaleSleeves,
    string BaseCurrency = "USD");

public record GetMonthlyIncomeLensQuery(Guid UserId) : IQuery<MonthlyIncomeLensResponse>;

/// <summary>
/// Reads through <see cref="ProjectionInputsReader"/>, the same reader the twelve-month and FIRE projections use,
/// so the lens starts from the same snapshot and the same withdrawal rate as the FIRE target.
/// </summary>
public class GetMonthlyIncomeLensQueryHandler(ProjectionInputsReader inputs)
    : IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse>
{
    private readonly ProjectionInputsReader _inputs = inputs;

    public async Task<MonthlyIncomeLensResponse> Handle(GetMonthlyIncomeLensQuery query, CancellationToken cancellationToken)
    {
        var inputs = await _inputs.ReadAsync(query.UserId, cancellationToken);
        var netWorth = inputs.Snapshot?.TotalNetWorth ?? 0m;
        var income = inputs.Snapshot is null ? null : IncomeLens.MonthlyIncome(netWorth, inputs.Assumptions);

        return new MonthlyIncomeLensResponse(
            income is null ? MonthlyIncomeLensStatus.NoNetWorth : MonthlyIncomeLensStatus.Shown,
            netWorth,
            inputs.Assumptions.SafeWithdrawalRate,
            income,
            inputs.HasStaleSleeves);
    }
}

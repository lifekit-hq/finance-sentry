namespace FinanceSentry.Modules.Budgets.Application.Services;

using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Domain;

public interface IBudgetEstimateService
{
    /// <summary>
    /// The estimate of <paramref name="year"/>-<paramref name="month"/> for the user, with the hand edits in
    /// <paramref name="overrides"/> laid over it. The caller supplies the overrides so a job with no person in scope
    /// can pass the ones it already read unscoped; every other read names the user.
    /// </summary>
    /// <param name="asOf">The moment the month is read at: a month in progress is paced to it.</param>
    Task<BudgetSummaryResponse> ComputeAsync(
        Guid userId,
        int year,
        int month,
        DateTime asOf,
        IReadOnlyList<Budget> overrides,
        CancellationToken ct = default);
}
